using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.ICEPAY;

public class ICEPAYSettings : ISettings
{
    public const string DefaultReferenceTemplate = "Order #{ORDER_ID}";
    public string MerchantId { get; set; } = "";
    public string EncryptedSecret { get; set; } = "";
    public string ReferenceTemplate { get; set; } = DefaultReferenceTemplate;
}
