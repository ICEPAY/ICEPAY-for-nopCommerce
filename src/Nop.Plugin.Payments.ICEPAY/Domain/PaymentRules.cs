using System.Globalization;
using ICEPAY.Checkout;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;

namespace Nop.Plugin.Payments.ICEPAY.Domain;

public static class PaymentRules
{
    // All currencies currently exposed by the SDK use two decimal minor units.
    public static readonly IReadOnlySet<string> Currencies =
        new HashSet<string>(["EUR", "GBP", "USD", "PLN", "SEK", "NOK", "DKK", "CZK"], StringComparer.Ordinal);

    public static int ToMinor(decimal amount, string currency)
    {
        if (!Currencies.Contains(currency) || amount <= 0)
            throw new InvalidOperationException("ICEPAY requires a positive amount in a supported currency.");
        var minor = checked((int)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero));
        return minor > 0 ? minor : throw new InvalidOperationException("The amount is below one minor currency unit.");
    }

    public static void Validate(Payment payment, ICEPAYPaymentAttempt attempt)
    {
        if (string.IsNullOrEmpty(payment.Key) ||
            (attempt.PaymentKey.Length > 0 && payment.Key != attempt.PaymentKey) ||
            payment.Reference != attempt.Reference ||
            payment.Merchant.Id.ToString(CultureInfo.InvariantCulture) != attempt.MerchantId ||
            payment.Amount.Value != attempt.MinorAmount ||
            !string.Equals(payment.Amount.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentValidationException("ICEPAY payment does not match its stored order, amount, currency or merchant.");
    }

    public static bool RequiresReview(Order order, ICEPAYPaymentAttempt attempt) =>
        order.Deleted || order.OrderStatus == OrderStatus.Cancelled ||
        order.OrderTotal != attempt.OrderTotal ||
        (order.PaymentStatus != PaymentStatus.Pending && order.CaptureTransactionId != attempt.PaymentKey) ||
        (!string.IsNullOrEmpty(order.CaptureTransactionId) && order.CaptureTransactionId != attempt.PaymentKey);

    public static int RefundMinor(ICEPAYPaymentAttempt attempt, decimal amount, decimal alreadyRefunded, int minorRefunded)
    {
        if (amount <= 0 || amount > attempt.OrderTotal - alreadyRefunded)
            throw new InvalidOperationException("The refund exceeds the remaining order balance.");
        var minor = amount == attempt.OrderTotal - alreadyRefunded
            ? attempt.MinorAmount - minorRefunded
            : checked((int)decimal.Round(attempt.MinorAmount * amount / attempt.OrderTotal, 0, MidpointRounding.AwayFromZero));
        if (minor <= 0 || minor > attempt.MinorAmount - minorRefunded)
            throw new InvalidOperationException("The refund exceeds the remaining ICEPAY balance or rounds below one minor unit.");
        return minor;
    }

    public static bool DefinitivelyRejected(Exception error) =>
        error is IcepayException { StatusCode: { } status } && (int)status is >= 400 and < 500 && (int)status != 408;
}

// Only controlled validation messages are safe to include in merchant-facing order notes.
public sealed class PaymentValidationException(string message) : InvalidOperationException(message);
