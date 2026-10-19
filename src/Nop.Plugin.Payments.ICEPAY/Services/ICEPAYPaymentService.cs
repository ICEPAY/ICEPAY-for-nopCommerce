using System.Globalization;
using ICEPAY.Checkout;
using Microsoft.Extensions.Caching.Memory;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.ICEPAY.Domain;
using Nop.Services.Configuration;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Stores;

namespace Nop.Plugin.Payments.ICEPAY.Services;

public class ICEPAYPaymentService(IICEPAYStore history, ICEPAYGateway gateway,
    IOrderService orders, IOrderProcessingService processing, ISettingService settings,
    IStoreService stores, IStoreContext storeContext, IWorkContext workContext,
    IMemoryCache cache, ILogger logger, IAddressService addresses, ICountryService countries)
{
    public async Task<bool> IsAvailableAsync(string apiType)
    {
        var store = await storeContext.GetCurrentStoreAsync();
        var config = await settings.LoadSettingAsync<ICEPAYSettings>(store.Id);
        var currency = await workContext.GetWorkingCurrencyAsync();
        if (string.IsNullOrEmpty(config.MerchantId) || string.IsNullOrEmpty(config.EncryptedSecret) ||
            !PaymentRules.Currencies.Contains(currency.CurrencyCode)) return false;
        try
        {
            // A changed credential produces a different cache entry without exposing it in a cache key.
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(config.EncryptedSecret)));
            var methods = await cache.GetOrCreateAsync($"ICEPAY.Methods.{store.Id}.{config.MerchantId}.{fingerprint}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return await gateway.Create(config.MerchantId, config.EncryptedSecret).PaymentMethods.ListAsync();
            });
            return methods?.Any(m => string.Equals(m.Id, apiType, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (Exception ex) when (ex is IcepayException or HttpRequestException or TaskCanceledException)
        {
            await logger.WarningAsync($"ICEPAY methods could not be loaded for store {store.Id}.");
            return false;
        }
    }

    public Task<string?> StartAsync(Order suppliedOrder, string apiType) => history.WithOrderLockAsync(suppliedOrder.Id, async () =>
    {
        var order = await orders.GetOrderByIdAsync(suppliedOrder.Id);
        if (order is null || order.Deleted || order.OrderStatus == OrderStatus.Cancelled)
            throw new InvalidOperationException("The order is no longer payable.");
        if (order.PaymentStatus != PaymentStatus.Pending) return null;
        var attempts = await history.AttemptsAsync(order.Id);
        foreach (var existing in attempts.Where(a => a.PaymentKey.Length > 0 && !a.ReviewRequired))
        {
            var payment = await gateway.Create(existing).Payments.GetAsync(existing.PaymentKey);
            await ApplyAsync(order, existing, payment);
            if (order.PaymentStatus != PaymentStatus.Pending) return null;
        }
        if (attempts.Any(a => a.ReviewRequired || a.CreationState is "Creating" or "Unknown"))
            throw new InvalidOperationException("An earlier ICEPAY request needs merchant review before retrying.");
        var pending = attempts.LastOrDefault(a => a.PaymentStatus is nameof(Status.Started) or nameof(Status.Pending));
        if (pending is not null)
        {
            if (string.IsNullOrEmpty(pending.CheckoutUrl)) throw new InvalidOperationException("The pending payment has no redirect link.");
            return pending.CheckoutUrl;
        }

        var config = await settings.LoadSettingAsync<ICEPAYSettings>(order.StoreId);
        if (!int.TryParse(config.MerchantId, out var merchantId) || merchantId <= 0 || string.IsNullOrEmpty(config.EncryptedSecret))
            throw new InvalidOperationException("ICEPAY credentials are not configured.");
        var methods = await gateway.Create(config.MerchantId, config.EncryptedSecret).PaymentMethods.ListAsync();
        if (!methods.Any(m => string.Equals(m.Id, apiType, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The chosen method is not available for this ICEPAY merchant.");
        var store = await stores.GetStoreByIdAsync(order.StoreId);
        if (!Uri.TryCreate(store.Url, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("ICEPAY requires the store URL to use HTTPS for redirects and callbacks.");
        var billing = await addresses.GetAddressByIdAsync(order.BillingAddressId);
        var country = billing is null ? null : await countries.GetCountryByAddressAsync(billing);
        var metadata = ICEPAYCheckoutData.Metadata(billing, country?.TwoLetterIsoCode);
        var attempt = new ICEPAYPaymentAttempt
        {
            OrderId = order.Id, StoreId = order.StoreId,
            Reference = ICEPAYCheckoutData.Reference(config.ReferenceTemplate, order), Method = apiType,
            MerchantId = merchantId.ToString(CultureInfo.InvariantCulture), EncryptedSecret = config.EncryptedSecret,
            Currency = order.CustomerCurrencyCode,
            MinorAmount = PaymentRules.ToMinor(order.OrderTotal * order.CurrencyRate, order.CustomerCurrencyCode),
            OrderTotal = order.OrderTotal
        };
        await history.InsertAsync(attempt);
        try
        {
            var baseUrl = store.Url.TrimEnd('/') + "/";
            var payment = await gateway.Create(attempt).Payments.CreateCheckoutAsync(new CheckoutCreateOptions
            {
                Reference = attempt.Reference, Description = $"Order {order.CustomOrderNumber}",
                Amount = new Amount(attempt.MinorAmount, attempt.Currency),
                PaymentMethod = new PaymentMethodDetails { Type = apiType },
                Meta = metadata,
                RedirectUrl = $"{baseUrl}Plugins/ICEPAY/Return/{attempt.Id}",
                WebhookUrl = $"{baseUrl}Plugins/ICEPAY/Webhook/{attempt.Id}"
            });
            // Persist the key even if validation fails, so an accepted payment remains recoverable.
            attempt.PaymentKey = payment.Key;
            await history.SaveAsync(attempt);
            PaymentRules.Validate(payment, attempt);
            var checkoutUrl = string.IsNullOrWhiteSpace(payment.Links.Direct) ? payment.Links.Checkout : payment.Links.Direct;
            if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var redirect) || redirect.Scheme != Uri.UriSchemeHttps)
                throw new PaymentValidationException("ICEPAY did not supply a valid HTTPS payment link.");
            attempt.CheckoutUrl = redirect.AbsoluteUri;
            attempt.CreationState = "Created";
            await ApplyAsync(order, attempt, payment);
            return order.PaymentStatus == PaymentStatus.Pending ? attempt.CheckoutUrl : null;
        }
        catch (Exception ex)
        {
            attempt.CreationState = PaymentRules.DefinitivelyRejected(ex) ? "Rejected" : "Unknown";
            attempt.ReviewRequired = attempt.CreationState == "Unknown";
            await history.SaveAsync(attempt);
            await NoteAsync(order.Id, $"ICEPAY payment creation {attempt.CreationState.ToLowerInvariant()}. Reference: {attempt.Reference}. " +
                (attempt.ReviewRequired ? "Check this reference in the ICEPAY Portal before another attempt; the request may have been accepted." : "ICEPAY rejected the request. Check the merchant configuration.") +
                (ex is PaymentValidationException validation ? " " + validation.Message : ""));
            throw new InvalidOperationException("ICEPAY payment creation was not confirmed. Review the order notes before retrying.");
        }
    });

    public async Task HandleWebhookAsync(int attemptId, string body, string? signature)
    {
        var attempt = await history.FindAttemptAsync(attemptId) ?? throw new KeyNotFoundException();
        var payment = gateway.Verify(body, signature, attempt);
        PaymentRules.Validate(payment, attempt);
        await RefreshAsync(attemptId, payment.Key);
    }

    public async Task RefreshAsync(int attemptId, string? verifiedKey = null)
    {
        var initial = await history.FindAttemptAsync(attemptId) ?? throw new KeyNotFoundException();
        await history.WithOrderLockAsync(initial.OrderId, async () =>
        {
            var attempt = (await history.FindAttemptAsync(attemptId))!;
            var key = attempt.PaymentKey.Length > 0 ? attempt.PaymentKey : verifiedKey;
            if (string.IsNullOrEmpty(key)) return false;
            var order = await orders.GetOrderByIdAsync(attempt.OrderId) ?? throw new KeyNotFoundException();
            // Read current state: delayed callbacks must never regress an order's status.
            var payment = await gateway.Create(attempt).Payments.GetAsync(key);
            await ApplyAsync(order, attempt, payment);
            return true;
        });
    }

    private async Task ApplyAsync(Order order, ICEPAYPaymentAttempt attempt, Payment payment)
    {
        PaymentRules.Validate(payment, attempt);
        attempt.PaymentKey = payment.Key;
        attempt.PaymentStatus = payment.Status.ToString();
        attempt.LastCheckedOnUtc = DateTime.UtcNow;
        if (attempt.CreationState is "Creating" or "Unknown")
        {
            attempt.CreationState = "Created";
            attempt.ReviewRequired = false;
        }
        if (payment.Status == Status.Completed && !attempt.Applied)
        {
            if (PaymentRules.RequiresReview(order, attempt))
            {
                if (!attempt.ReviewRequired)
                {
                    await NoteAsync(order.Id, $"ICEPAY payment {payment.Key} completed but requires manual review (cancelled, changed or already paid order). " +
                        "Order status was preserved. Review/refund this payment explicitly in the ICEPAY Portal.");
                    await logger.ErrorAsync($"ICEPAY payment {payment.Key} requires manual review for order {order.Id}.");
                }
                attempt.ReviewRequired = true;
            }
            else if (order.PaymentStatus == PaymentStatus.Pending)
            {
                order.CaptureTransactionId = payment.Key;
                order.CaptureTransactionResult = "ICEPAY completed";
                // Per agreed policy, Completed is sufficient regardless of financialStatus.
                await processing.MarkOrderAsPaidAsync(order);
                attempt.Applied = true;
            }
            else if (order.CaptureTransactionId == payment.Key)
                attempt.Applied = true;
        }
        // Expired/cancelled attempts leave the order pending; a late completion is still possible.
        await history.SaveAsync(attempt);
    }

    public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request) => history.WithOrderLockAsync(request.Order.Id, async () =>
    {
        var order = await orders.GetOrderByIdAsync(request.Order.Id) ?? throw new KeyNotFoundException();
        var attempt = (await history.AttemptsAsync(order.Id)).SingleOrDefault(a => a.PaymentKey == order.CaptureTransactionId && a.Applied)
            ?? throw new InvalidOperationException("No completed ICEPAY payment is recorded for this order.");
        var refunds = await history.RefundsAsync(order.Id);
        if (refunds.Any(r => r.State is "Submitting" or "Unknown"))
            throw new InvalidOperationException("An earlier refund has an unknown result. Check ICEPAY before submitting another refund.");
        var accepted = refunds.Where(r => r.State == "Accepted").ToList();
        var acceptedBase = accepted.Sum(r => r.BaseAmount);
        if (acceptedBase > order.RefundedAmount)
            throw new InvalidOperationException("An accepted ICEPAY refund has not yet been recorded by nopCommerce. Refresh or reconcile the order first.");
        if (attempt.OrderTotal != order.OrderTotal)
            throw new InvalidOperationException("The order total changed after payment. Reconcile the order before refunding.");
        // Offline refunds may have been recorded against this same payment. Include them in both balances.
        var offlineBase = order.RefundedAmount - acceptedBase;
        var minorRefunded = accepted.Sum(r => r.MinorAmount) + checked((int)decimal.Round(
            attempt.MinorAmount * offlineBase / attempt.OrderTotal, 0, MidpointRounding.AwayFromZero));
        var refund = new ICEPAYRefundRequest
        {
            OrderId = order.Id, AttemptId = attempt.Id, Reference = $"nop-refund-{Guid.NewGuid():N}",
            BaseAmount = request.AmountToRefund,
            MinorAmount = PaymentRules.RefundMinor(attempt, request.AmountToRefund, order.RefundedAmount, minorRefunded)
        };
        await history.InsertAsync(refund);
        try
        {
            var response = await gateway.Create(attempt).Refunds.CreateAsync(attempt.PaymentKey, new RefundCreateOptions
            {
                Reference = refund.Reference, Description = $"Refund for order {order.CustomOrderNumber}",
                Amount = new Amount(refund.MinorAmount, attempt.Currency)
            });
            // A successful API creation is success, irrespective of the returned refund status.
            refund.State = "Accepted";
            refund.RefundKey = response.Key ?? "";
            await history.SaveAsync(refund);
            request.Order.RefundedAmount = order.RefundedAmount;
            return new RefundPaymentResult
            {
                NewPaymentStatus = order.RefundedAmount + request.AmountToRefund == order.OrderTotal
                    ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded
            };
        }
        catch (Exception ex)
        {
            refund.State = PaymentRules.DefinitivelyRejected(ex) ? "Rejected" : "Unknown";
            await history.SaveAsync(refund);
            await NoteAsync(order.Id, $"ICEPAY refund {refund.State.ToLowerInvariant()}. Reference: {refund.Reference}. " +
                "Check the ICEPAY Portal before retrying if the outcome is unknown.");
            throw new InvalidOperationException("ICEPAY refund creation was not confirmed. Review the order notes before retrying.");
        }
    });

    public Task NoteAsync(int orderId, string message) => orders.InsertOrderNoteAsync(new OrderNote
    {
        OrderId = orderId, Note = message, DisplayToCustomer = false, CreatedOnUtc = DateTime.UtcNow
    });
}
