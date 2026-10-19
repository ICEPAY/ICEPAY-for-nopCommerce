using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.ICEPAY.Models;

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }
    [NopResourceDisplayName("Plugins.Payments.ICEPAY.Override")]
    public bool OverrideForStore { get; set; }
    [NopResourceDisplayName("Plugins.Payments.ICEPAY.MerchantId")]
    public string MerchantId { get; set; } = "";
    [NopResourceDisplayName("Plugins.Payments.ICEPAY.Secret")]
    public string? Secret { get; set; }
    [NopResourceDisplayName("Plugins.Payments.ICEPAY.ReferenceTemplate")]
    public string? ReferenceTemplate { get; set; } = ICEPAYSettings.DefaultReferenceTemplate;
    public bool ReferenceTemplate_OverrideForStore { get; set; }
}
