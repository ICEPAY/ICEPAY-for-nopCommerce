using System.Net;
using System.Text.Json;
using ICEPAY.Checkout;
using Nop.Core.Domain.Payments;
using Nop.Services.Payments;
using NUnit.Framework;

namespace ICEPAY.Tests;

public class RefundTests
{
    [TestCase(Status.Pending)]
    [TestCase(Status.Completed)]
    [TestCase(Status.Started)]
    [TestCase(Status.Cancelled)]
    [TestCase(Status.Expired)]
    public async Task Accepted_creation_is_success_regardless_of_returned_status(Status status)
    {
        using var f = new Fixture();
        f.Http.Respond = _ => Fixture.Response(new Refund { Key = "refund-one", Status = status });
        var result = await f.Service.RefundAsync(new RefundPaymentRequest { Order = f.Order, AmountToRefund = 10m });
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.NewPaymentStatus, Is.EqualTo(PaymentStatus.Refunded));
            Assert.That(f.History.Refunds.Single().State, Is.EqualTo("Accepted"));
            Assert.That(f.Http.Requests.Single().Path, Is.EqualTo("/api/payments/payment-one/refund"));
            Assert.That(f.Http.Requests.Single().Auth, Is.EqualTo("Basic MTIzOnNlY3JldA=="));
        });
        using var body = JsonDocument.Parse(f.Http.Requests.Single().Body);
        Assert.That(body.RootElement.GetProperty("amount").GetProperty("value").GetInt32(), Is.EqualTo(1230));
        Assert.That(body.RootElement.GetProperty("amount").GetProperty("currency").GetString(), Is.EqualTo("GBP"));
    }

    [Test]
    public async Task Partial_then_final_refund_consumes_the_original_minor_unit_balance()
    {
        using var f = new Fixture();
        f.Http.Respond = _ => Fixture.Response(new Refund { Key = Guid.NewGuid().ToString(), Status = Status.Pending });
        var first = await f.Service.RefundAsync(new RefundPaymentRequest { Order = f.Order, AmountToRefund = 3.33m, IsPartialRefund = true });
        Assert.That(first.NewPaymentStatus, Is.EqualTo(PaymentStatus.PartiallyRefunded));
        f.Order.RefundedAmount = 3.33m; // nopCommerce records the successful result.
        f.Order.CurrencyRate = 5m; // Later exchange-rate changes must not affect refunds.
        var last = await f.Service.RefundAsync(new RefundPaymentRequest { Order = f.Order, AmountToRefund = 6.67m, IsPartialRefund = true });
        Assert.That(last.NewPaymentStatus, Is.EqualTo(PaymentStatus.Refunded));
        Assert.That(f.History.Refunds.Select(r => r.MinorAmount), Is.EqualTo(new[] { 410, 820 }));
    }

    [TestCase(400, "Rejected")]
    [TestCase(401, "Rejected")]
    [TestCase(500, "Unknown")]
    [TestCase(408, "Unknown")]
    public void Failed_creation_never_reports_success(int code, string expectedState)
    {
        using var f = new Fixture();
        f.Http.Respond = _ => Fixture.Response(new { message = "Failure" }, (HttpStatusCode)code);
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RefundAsync(new RefundPaymentRequest { Order = f.Order, AmountToRefund = 10m }));
        Assert.That(f.History.Refunds.Single().State, Is.EqualTo(expectedState));
        Assert.That(f.Order.RefundedAmount, Is.Zero);
    }

    [Test]
    public void Lost_response_blocks_automatic_resubmission()
    {
        using var f = new Fixture();
        f.Http.Respond = _ => throw new HttpRequestException("Connection lost");
        var request = new RefundPaymentRequest { Order = f.Order, AmountToRefund = 5m, IsPartialRefund = true };
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RefundAsync(request));
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RefundAsync(request));
        Assert.That(f.Http.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Concurrent_refund_cannot_repeat_an_accepted_request_before_nop_records_it()
    {
        using var f = new Fixture();
        f.Http.Respond = _ => Fixture.Response(new Refund { Key = "refund-one", Status = Status.Pending });
        var request = new RefundPaymentRequest { Order = f.Order, AmountToRefund = 10m };
        var first = f.Service.RefundAsync(request);
        var second = f.Service.RefundAsync(request);
        Assert.That((await first).Success, Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
        Assert.That(f.Http.Requests, Has.Count.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(11)]
    public void Invalid_amount_never_reaches_the_gateway(decimal amount)
    {
        using var f = new Fixture();
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RefundAsync(new RefundPaymentRequest { Order = f.Order, AmountToRefund = amount }));
        Assert.That(f.Http.Requests, Is.Empty);
    }
}
