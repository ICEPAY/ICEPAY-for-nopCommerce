using System.Text.Json;
using ICEPAY.Checkout;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.ICEPAY;
using NUnit.Framework;

namespace ICEPAY.Tests;

public class CheckoutTests
{
    [TestCase("gbp", "https://bank.example/pay", null, "https://bank.example/pay")]
    [TestCase("GBP", null, "https://checkout.icepay.com/pay", "https://checkout.icepay.com/pay")]
    [TestCase("gbp", null, "https://checkout.icepay.com/pay", "https://checkout.icepay.com/pay")]
    [TestCase("gbp", "", "https://checkout.icepay.com/pay", "https://checkout.icepay.com/pay")]
    [TestCase("gbp", "https://bank.example/pay", "https://checkout.icepay.com/pay", "https://bank.example/pay")]
    public async Task Checkout_accepts_api_currency_casing_and_hosted_checkout_links(
        string currency, string? direct, string? checkout, string expected)
    {
        using var f = new Fixture();
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Http.Respond = request =>
        {
            if (request.Method == HttpMethod.Get)
                return Fixture.Response(new[] { new PaymentMethod { Id = "ideal" } });
            var payment = f.Payment(Status.Started);
            payment.Reference = f.History.Attempts.Single().Reference;
            payment.Amount.Currency = currency;
            payment.Links = new Links { Direct = direct, Checkout = checkout };
            return Fixture.Response(payment);
        };

        Assert.That(await f.Service.StartAsync(f.Order, "ideal"), Is.EqualTo(expected));
        var attempt = f.History.Attempts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(attempt.CreationState, Is.EqualTo("Created"));
            Assert.That(attempt.ReviewRequired, Is.False);
            Assert.That(attempt.CheckoutUrl, Is.EqualTo(expected));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Checkout_accepts_the_merchant_processing_mode(bool isTest)
    {
        using var f = new Fixture();
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Http.Respond = request =>
        {
            if (request.Method == HttpMethod.Get)
                return Fixture.Response(new[] { new PaymentMethod { Id = "ideal" } });
            var payment = f.Payment(Status.Started);
            payment.Reference = f.History.Attempts.Single().Reference;
            payment.IsTest = isTest;
            return Fixture.Response(payment);
        };

        Assert.That(await f.Service.StartAsync(f.Order, "ideal"), Is.EqualTo("https://bank.example/pay"));
        Assert.That(f.Notes, Is.Empty);
        Assert.That(f.History.Attempts.Single().ReviewRequired, Is.False);
        Assert.That(f.Http.Requests.Count(r => r.Method == "POST"), Is.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("http://checkout.icepay.com/pay")]
    public void Checkout_requires_a_valid_https_link(string? checkout)
    {
        using var f = new Fixture();
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Http.Respond = request =>
        {
            if (request.Method == HttpMethod.Get)
                return Fixture.Response(new[] { new PaymentMethod { Id = "ideal" } });
            var payment = f.Payment(Status.Started);
            payment.Reference = f.History.Attempts.Single().Reference;
            payment.Links = new Links { Checkout = checkout };
            return Fixture.Response(payment);
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Order, "ideal"));
        Assert.That(f.History.Attempts.Single().ReviewRequired, Is.True);
    }

    [Test]
    public async Task Checkout_uses_order_currency_snapshot_and_selected_method()
    {
        using var f = new Fixture();
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Http.Respond = request =>
        {
            if (request.Method == HttpMethod.Get) return Fixture.Response(new[] { new PaymentMethod { Id = "card" } });
            var attempt = f.History.Attempts.Single();
            var result = f.Payment(Status.Started);
            result.Reference = attempt.Reference;
            return Fixture.Response(result);
        };
        var redirect = await f.Service.StartAsync(f.Order, "card");
        Assert.That(redirect, Is.EqualTo("https://bank.example/pay"));
        var request = f.Http.Requests.Single(r => r.Method == "POST");
        using var body = JsonDocument.Parse(request.Body);
        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("amount").GetProperty("value").GetInt32(), Is.EqualTo(1230));
            Assert.That(body.RootElement.GetProperty("amount").GetProperty("currency").GetString(), Is.EqualTo("GBP"));
            Assert.That(body.RootElement.GetProperty("paymentMethod").GetProperty("type").GetString(), Is.EqualTo("card"));
            Assert.That(body.RootElement.GetProperty("webhookUrl").GetString(), Is.EqualTo("https://shop.example/Plugins/ICEPAY/Webhook/1"));
            Assert.That(f.History.Attempts.Single().CreationState, Is.EqualTo("Created"));
        });
    }

    [Test]
    public async Task Retry_reuses_existing_pending_payment()
    {
        using var f = new Fixture();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Attempt.Applied = false;
        f.Http.Respond = _ => Fixture.Response(f.Payment(Status.Pending));
        Assert.That(await f.Service.StartAsync(f.Order, "ideal"), Is.EqualTo("https://bank.example/pay"));
        Assert.That(f.Http.Requests.Select(r => r.Method), Is.EqualTo(new[] { "GET" }));
    }

    [Test]
    public async Task Retry_refreshes_a_completed_payment_instead_of_creating_another()
    {
        using var f = new Fixture();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Attempt.Applied = false;
        f.Http.Respond = _ => Fixture.Response(f.Payment());
        Assert.That(await f.Service.StartAsync(f.Order, "ideal"), Is.Null);
        Assert.That(f.Order.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(f.Http.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void Uncertain_creation_is_not_automatically_retried()
    {
        using var f = new Fixture();
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Http.Respond = request => request.Method == HttpMethod.Get
            ? Fixture.Response(new[] { new PaymentMethod { Id = "ideal" } })
            : throw new HttpRequestException("Response lost after request was sent");
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Order, "ideal"));
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Order, "ideal"));
        Assert.That(f.Http.Requests.Count(r => r.Method == "POST"), Is.EqualTo(1));
        Assert.That(f.History.Attempts.Single().ReviewRequired, Is.True);
    }

    [Test]
    public void Unsupported_currency_cannot_create_a_payment()
    {
        using var f = new Fixture();
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CustomerCurrencyCode = "JPY";
        f.Http.Respond = _ => Fixture.Response(new[] { new PaymentMethod { Id = "ideal" } });
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Order, "ideal"));
        Assert.That(f.Http.Requests.Any(r => r.Method == "POST"), Is.False);
    }

    [Test]
    public void Payment_methods_include_both_card_aliases()
    {
        Assert.That(PaymentMethods.All.Select(m => m.ApiType), Is.EqualTo(new[]
            { "ideal", "card", "bancontact", "paypal", "onlineueberweisen", "eps", "card", "card", "paybybank" }));
    }

    [Test]
    public void Configuration_view_is_compiled_at_the_plugin_path()
    {
        var items = new Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemLoader().LoadItems(typeof(ICEPAYCorePlugin).Assembly);
        Assert.That(items.Select(i => i.Identifier), Does.Contain("/Plugins/Payments.ICEPAY.Core/Views/Configure.cshtml"));
    }
}
