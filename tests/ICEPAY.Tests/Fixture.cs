using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ICEPAY.Checkout;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Payments.ICEPAY;
using Nop.Plugin.Payments.ICEPAY.Domain;
using Nop.Plugin.Payments.ICEPAY.Services;
using Nop.Services.Configuration;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Stores;

namespace ICEPAY.Tests;

internal sealed class MemoryHistory : IICEPAYStore
{
    private readonly SemaphoreSlim _mutex = new(1);
    public readonly List<ICEPAYPaymentAttempt> Attempts = [];
    public readonly List<ICEPAYRefundRequest> Refunds = [];
    public async Task<T> WithOrderLockAsync<T>(int id, Func<Task<T>> action)
    {
        await _mutex.WaitAsync();
        try { return await action(); } finally { _mutex.Release(); }
    }
    public Task<ICEPAYPaymentAttempt?> FindAttemptAsync(int id) => Task.FromResult(Attempts.SingleOrDefault(a => a.Id == id));
    public Task<IList<ICEPAYPaymentAttempt>> AttemptsAsync(int id) => Task.FromResult<IList<ICEPAYPaymentAttempt>>(Attempts.Where(a => a.OrderId == id).ToList());
    public Task<IList<ICEPAYPaymentAttempt>> PendingAsync() => Task.FromResult<IList<ICEPAYPaymentAttempt>>(Attempts.Where(a => !a.Applied && !a.ReviewRequired).ToList());
    public Task TouchAsync(int id) { Attempts.Single(a => a.Id == id).LastCheckedOnUtc = DateTime.UtcNow; return Task.CompletedTask; }
    public Task InsertAsync(ICEPAYPaymentAttempt item) { item.Id = Attempts.Count + 1; Attempts.Add(item); return Task.CompletedTask; }
    public Task SaveAsync(ICEPAYPaymentAttempt item) => Task.CompletedTask;
    public Task<IList<ICEPAYRefundRequest>> RefundsAsync(int id) => Task.FromResult<IList<ICEPAYRefundRequest>>(Refunds.Where(r => r.OrderId == id).ToList());
    public Task InsertAsync(ICEPAYRefundRequest item) { item.Id = Refunds.Count + 1; Refunds.Add(item); return Task.CompletedTask; }
    public Task SaveAsync(ICEPAYRefundRequest item) => Task.CompletedTask;
}

internal sealed class FakeHttp : HttpMessageHandler
{
    public readonly List<(string Method, string Path, string Body, string? Auth)> Requests = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => throw new InvalidOperationException("Unexpected HTTP request");
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath,
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.Authorization?.ToString()));
        return Respond(request);
    }
}

internal sealed class Fixture : IDisposable
{
    public readonly MemoryHistory History = new();
    public readonly FakeHttp Http = new();
    public readonly Mock<IOrderService> Orders = new();
    public readonly Mock<IOrderProcessingService> Processing = new();
    public readonly Mock<ISettingService> Settings = new();
    public readonly Mock<IStoreService> Stores = new();
    public readonly Mock<IAddressService> Addresses = new();
    public readonly Mock<ICountryService> Countries = new();
    public readonly ICEPAYSettings Config = new() { MerchantId = "123", EncryptedSecret = "encrypted" };
    public readonly Address BillingAddress = new()
    {
        Id = 4, Email = "buyer@example.test", CountryId = 1, City = "Amsterdam",
        ZipPostalCode = "1012 AB", Address1 = "Damrak 1", Address2 = "Unit 2"
    };
    public readonly MemoryCache Cache = new(new MemoryCacheOptions());
    public readonly List<string> Notes = [];
    public readonly Order Order = new()
    {
        Id = 42, StoreId = 2, CustomerId = 3, BillingAddressId = 4, OrderTotal = 10m, CurrencyRate = 1.23m,
        CustomerCurrencyCode = "GBP", PaymentStatus = PaymentStatus.Paid,
        OrderStatus = OrderStatus.Processing, CaptureTransactionId = "payment-one", CustomOrderNumber = "42"
    };
    public readonly ICEPAYPaymentAttempt Attempt = new()
    {
        Id = 1, OrderId = 42, StoreId = 2, Reference = "reference-one", PaymentKey = "payment-one",
        MerchantId = "123", EncryptedSecret = "encrypted", Method = "ideal",
        Currency = "GBP", MinorAmount = 1230, OrderTotal = 10m, Applied = true,
        CreationState = "Created", PaymentStatus = "Completed", CheckoutUrl = "https://bank.example/pay"
    };
    public readonly ICEPAYPaymentService Service;
    public readonly ICEPAYGateway Gateway;
    private readonly HttpClient _client;
    public Fixture()
    {
        History.Attempts.Add(Attempt);
        Orders.Setup(x => x.GetOrderByIdAsync(42)).ReturnsAsync(() => Order);
        Orders.Setup(x => x.InsertOrderNoteAsync(It.IsAny<OrderNote>())).Callback<OrderNote>(n => Notes.Add(n.Note)).Returns(Task.CompletedTask);
        Processing.Setup(x => x.MarkOrderAsPaidAsync(It.IsAny<Order>())).Callback<Order>(o => o.PaymentStatus = PaymentStatus.Paid).Returns(Task.CompletedTask);
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(x => x.DecryptText("encrypted", "")).Returns("secret");
        var factory = new Mock<IHttpClientFactory>();
        _client = new HttpClient(Http);
        factory.Setup(x => x.CreateClient("ICEPAY")).Returns(_client);
        Gateway = new ICEPAYGateway(factory.Object, encryption.Object);
        Stores.Setup(x => x.GetStoreByIdAsync(2)).ReturnsAsync(new Store { Id = 2, Url = "https://shop.example/" });
        Settings.Setup(x => x.LoadSettingAsync<ICEPAYSettings>(2)).ReturnsAsync(Config);
        Addresses.Setup(x => x.GetAddressByIdAsync(4)).ReturnsAsync(BillingAddress);
        Countries.Setup(x => x.GetCountryByAddressAsync(BillingAddress)).ReturnsAsync(new Country { TwoLetterIsoCode = "NL" });
        Service = new ICEPAYPaymentService(History, Gateway, Orders.Object, Processing.Object, Settings.Object,
            Stores.Object, Mock.Of<IStoreContext>(), Mock.Of<IWorkContext>(), Cache, Mock.Of<ILogger>(), Addresses.Object, Countries.Object);
    }
    public Payment Payment(Status status = Status.Completed) => new()
    {
        Key = Attempt.PaymentKey, Reference = Attempt.Reference, Amount = new Amount(Attempt.MinorAmount, Attempt.Currency.ToLowerInvariant()),
        Merchant = new Merchant { Id = 123 }, IsTest = true, Status = status,
        Links = new Links { Direct = "https://bank.example/pay" }
    };
    public static string Json(object item) => JsonSerializer.Serialize(item, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    });
    public static HttpResponseMessage Response(object item, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(Json(item), Encoding.UTF8, "application/json") };
    public string Sign(string body) => Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes(body)));
    public void Dispose() { _client.Dispose(); Cache.Dispose(); }
}
