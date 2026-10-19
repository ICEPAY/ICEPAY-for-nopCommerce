using ICEPAY.Checkout;
using Nop.Plugin.Payments.ICEPAY.Domain;
using Nop.Services.Security;

namespace Nop.Plugin.Payments.ICEPAY.Services;

public class ICEPAYGateway(IHttpClientFactory clients, IEncryptionService encryption)
{
    public IcepayClient Create(string merchantId, string encryptedSecret) => new(new IcepayClientOptions
    {
        MerchantId = merchantId,
        MerchantSecret = encryption.DecryptText(encryptedSecret),
        HttpClient = clients.CreateClient("ICEPAY")
    });

    public IcepayClient Create(ICEPAYPaymentAttempt attempt) => Create(attempt.MerchantId, attempt.EncryptedSecret);
    public Payment Verify(string body, string? signature, ICEPAYPaymentAttempt attempt) =>
        WebhookUtility.ConstructEvent(body, signature, encryption.DecryptText(attempt.EncryptedSecret));
}
