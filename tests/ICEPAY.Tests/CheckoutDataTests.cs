using System.Text.Json;
using ICEPAY.Checkout;
using Moq;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Payments;
using NUnit.Framework;

namespace ICEPAY.Tests;

public class CheckoutDataTests
{
    [TestCase("Shop / {ORDER_ID}", "WEB-0042", "Shop / WEB-0042")]
    [TestCase("Order #{ORDER_ID}", "42", "Order #42")]
    [TestCase(" ", "42", "Order #42")]
    [TestCase(null, "42", "Order #42")]
    [TestCase("{ORDER_ID}", "", "42")]
    public async Task Reference_template_is_expanded_and_saved_with_the_attempt(string? template, string number, string expected)
    {
        using var f = new Fixture();
        f.Config.ReferenceTemplate = template!;
        f.Order.CustomOrderNumber = number;
        using var payload = await CreatePaymentAsync(f);
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("reference").GetString(), Is.EqualTo(expected));
            Assert.That(f.History.Attempts.Single().Reference, Is.EqualTo(expected));
        });
        f.Config.ReferenceTemplate = "Changed {ORDER_ID}";
        var saved = f.History.Attempts.Single();
        f.Http.Respond = _ => Fixture.Response(new Payment
        {
            Key = saved.PaymentKey, Reference = saved.Reference, Merchant = new Merchant { Id = 123 },
            Amount = new Amount(saved.MinorAmount, saved.Currency), Status = Status.Pending
        });
        Assert.That(await f.Service.StartAsync(f.Order, "ideal"), Is.EqualTo(saved.CheckoutUrl));
        Assert.That(f.Http.Requests.Count(r => r.Method == "POST"), Is.EqualTo(1));
    }

    [Test]
    public async Task Sdk_serializes_order_billing_details_and_integration_metadata()
    {
        using var f = new Fixture();
        using var payload = await CreatePaymentAsync(f);
        var meta = payload.RootElement.GetProperty("meta");
        var customer = meta.GetProperty("customer");
        var address = customer.GetProperty("address");
        var integration = meta.GetProperty("integration");
        Assert.Multiple(() =>
        {
            Assert.That(customer.GetProperty("email").GetString(), Is.EqualTo("buyer@example.test"));
            Assert.That(address.GetProperty("country").GetString(), Is.EqualTo("NL"));
            Assert.That(address.GetProperty("city").GetString(), Is.EqualTo("Amsterdam"));
            Assert.That(address.GetProperty("postalCode").GetString(), Is.EqualTo("1012 AB"));
            Assert.That(address.GetProperty("streetName").GetString(), Is.EqualTo("Damrak 1, Unit 2"));
            Assert.That(address.TryGetProperty("houseNumber", out _), Is.False, "Address line 2 is not a house number.");
            Assert.That(integration.GetProperty("type").GetString(), Is.EqualTo("nopcommerce"));
            Assert.That(integration.GetProperty("version").GetString(), Is.EqualTo("1.0.0"));
            Assert.That(integration.GetProperty("developer").GetString(), Is.EqualTo("ICEPAY"));
        });
    }

    [Test]
    public async Task Missing_billing_fields_are_omitted_without_blocking_checkout()
    {
        using var f = new Fixture();
        f.Addresses.Setup(x => x.GetAddressByIdAsync(4)).ReturnsAsync(new Address { Email = "  buyer@example.test  " });
        using var payload = await CreatePaymentAsync(f);
        var customer = payload.RootElement.GetProperty("meta").GetProperty("customer");
        Assert.That(customer.GetProperty("email").GetString(), Is.EqualTo("buyer@example.test"));
        Assert.That(customer.TryGetProperty("address", out _), Is.False);
    }

    [Test]
    public async Task Missing_billing_address_keeps_integration_metadata()
    {
        using var f = new Fixture();
        f.Addresses.Setup(x => x.GetAddressByIdAsync(4)).ReturnsAsync((Address?)null);
        using var payload = await CreatePaymentAsync(f);
        var meta = payload.RootElement.GetProperty("meta");
        Assert.That(meta.TryGetProperty("customer", out _), Is.False);
        Assert.That(meta.GetProperty("integration").GetProperty("type").GetString(), Is.EqualTo("nopcommerce"));
    }

    [Test]
    public async Task Billing_fields_are_bounded_without_splitting_unicode_characters()
    {
        using var f = new Fixture();
        f.BillingAddress.Email = new string('a', 140);
        f.BillingAddress.City = string.Concat(Enumerable.Repeat("😀", 260));
        f.BillingAddress.ZipPostalCode = new string('p', 40);
        f.BillingAddress.Address1 = new string('s', 150);
        using var payload = await CreatePaymentAsync(f);
        var customer = payload.RootElement.GetProperty("meta").GetProperty("customer");
        var address = customer.GetProperty("address");
        Assert.Multiple(() =>
        {
            Assert.That(customer.GetProperty("email").GetString(), Has.Length.EqualTo(128));
            Assert.That(address.GetProperty("city").GetString(), Is.EqualTo(string.Concat(Enumerable.Repeat("😀", 254))));
            Assert.That(address.GetProperty("postalCode").GetString(), Has.Length.EqualTo(31));
            Assert.That(address.GetProperty("streetName").GetString(), Has.Length.EqualTo(128));
        });
    }

    private static async Task<JsonDocument> CreatePaymentAsync(Fixture f)
    {
        f.History.Attempts.Clear();
        f.Order.PaymentStatus = PaymentStatus.Pending;
        f.Order.CaptureTransactionId = "";
        f.Http.Respond = request =>
        {
            if (request.Method == HttpMethod.Get) return Fixture.Response(new[] { new PaymentMethod { Id = "ideal" } });
            var payment = f.Payment(Status.Started);
            payment.Reference = f.History.Attempts.Single().Reference;
            return Fixture.Response(payment);
        };
        Assert.That(await f.Service.StartAsync(f.Order, "ideal"), Is.EqualTo("https://bank.example/pay"));
        return JsonDocument.Parse(f.Http.Requests.Single(r => r.Method == "POST").Body);
    }
}
