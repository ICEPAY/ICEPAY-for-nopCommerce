using ICEPAY.Checkout;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using NUnit.Framework;

namespace ICEPAY.Tests;

public class CallbackTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task Duplicate_completed_webhooks_mark_paid_once(bool isTest)
    {
        using var f = new Fixture();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Attempt.Applied = false;
        var payment = f.Payment();
        payment.IsTest = isTest;
        f.Http.Respond = _ => Fixture.Response(payment);
        var body = Fixture.Json(payment);
        await f.Service.HandleWebhookAsync(1, body, f.Sign(body));
        await f.Service.HandleWebhookAsync(1, body, f.Sign(body));
        f.Processing.Verify(x => x.MarkOrderAsPaidAsync(It.IsAny<Order>()), Times.Once);
        Assert.That(f.Order.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [Test]
    public async Task Delayed_expired_webhook_uses_latest_api_state()
    {
        using var f = new Fixture();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Attempt.Applied = false;
        f.Http.Respond = _ => Fixture.Response(f.Payment());
        var body = Fixture.Json(f.Payment(Status.Expired));
        await f.Service.HandleWebhookAsync(1, body, f.Sign(body));
        Assert.That(f.Order.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [Test]
    public async Task Completed_payment_on_cancelled_order_is_flagged_without_reopening()
    {
        using var f = new Fixture();
        f.Order.OrderStatus = OrderStatus.Cancelled;
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Attempt.Applied = false;
        f.Http.Respond = _ => Fixture.Response(f.Payment());
        await f.Service.RefreshAsync(1);
        Assert.That(f.Attempt.ReviewRequired, Is.True);
        Assert.That(f.Notes, Has.Count.EqualTo(1));
        Assert.That(f.Order.OrderStatus, Is.EqualTo(OrderStatus.Cancelled));
        f.Processing.Verify(x => x.MarkOrderAsPaidAsync(It.IsAny<Order>()), Times.Never);
    }

    [Test]
    public async Task Additional_successful_attempt_never_replaces_original_payment()
    {
        using var f = new Fixture();
        f.Order.CaptureTransactionId = "a-different-payment";
        f.Attempt.Applied = false;
        f.Http.Respond = _ => Fixture.Response(f.Payment());
        await f.Service.RefreshAsync(1);
        Assert.That(f.Attempt.ReviewRequired, Is.True);
        Assert.That(f.Order.CaptureTransactionId, Is.EqualTo("a-different-payment"));
    }

    [Test]
    public async Task Refund_status_is_never_reverted_by_payment_callbacks()
    {
        using var f = new Fixture();
        f.Order.PaymentStatus = PaymentStatus.Refunded;
        f.Order.RefundedAmount = 10m;
        f.Http.Respond = _ => Fixture.Response(f.Payment());
        await f.Service.RefreshAsync(1);
        Assert.That(f.Order.PaymentStatus, Is.EqualTo(PaymentStatus.Refunded));
        Assert.That(f.Order.RefundedAmount, Is.EqualTo(10m));
    }

    [Test]
    public void Invalid_signature_is_rejected_before_any_API_call()
    {
        using var f = new Fixture();
        Assert.ThrowsAsync<IcepayException>(() => f.Service.HandleWebhookAsync(1, Fixture.Json(f.Payment()), "invalid"));
        Assert.That(f.Http.Requests, Is.Empty);
    }

    [TestCase("amount")]
    [TestCase("currency")]
    [TestCase("merchant")]
    [TestCase("reference")]
    [TestCase("key")]
    public void Signed_but_mismatched_payment_is_rejected(string field)
    {
        using var f = new Fixture();
        var payment = f.Payment();
        switch (field)
        {
            case "amount": payment.Amount.Value++; break;
            case "currency": payment.Amount.Currency = "EUR"; break;
            case "merchant": payment.Merchant.Id++; break;
            case "reference": payment.Reference = "other-order"; break;
            case "key": payment.Key = "other-payment"; break;
        }
        var body = Fixture.Json(payment);
        Assert.CatchAsync<InvalidOperationException>(() => f.Service.HandleWebhookAsync(1, body, f.Sign(body)));
        Assert.That(f.Http.Requests, Is.Empty);
    }
}
