using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.ICEPAY.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.ICEPAY.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class ICEPAYConfigurationController(ISettingService settings, IStoreContext stores,
    IEncryptionService encryption, ILocalizationService localization, INotificationService notifications) : BasePaymentController
{
    private const string ViewPath = "~/Plugins/Payments.ICEPAY.Core/Views/Configure.cshtml";

    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure()
    {
        var scope = await stores.GetActiveStoreScopeConfigurationAsync();
        var config = await settings.LoadSettingAsync<ICEPAYSettings>(scope);
        return View(ViewPath, new ConfigurationModel
        {
            ActiveStoreScopeConfiguration = scope,
            MerchantId = config.MerchantId,
            ReferenceTemplate = config.ReferenceTemplate,
            ReferenceTemplate_OverrideForStore = scope > 0 && await settings.SettingExistsAsync(config, x => x.ReferenceTemplate, scope),
            OverrideForStore = scope > 0 && await settings.SettingExistsAsync(config, x => x.MerchantId, scope)
        });
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        var scope = await stores.GetActiveStoreScopeConfigurationAsync();
        var config = await settings.LoadSettingAsync<ICEPAYSettings>(scope);
        model.ActiveStoreScopeConfiguration = scope;
        if (scope == 0 || model.OverrideForStore)
        {
            if (!int.TryParse(model.MerchantId, out var merchant) || merchant <= 0 ||
                (string.IsNullOrWhiteSpace(model.Secret) && (string.IsNullOrEmpty(config.EncryptedSecret) || config.MerchantId != model.MerchantId)))
                ModelState.AddModelError("", await localization.GetResourceAsync("Plugins.Payments.ICEPAY.InvalidCredentials"));
        }
        if (!ModelState.IsValid)
        {
            model.Secret = null;
            ModelState.Remove(nameof(model.Secret));
            return View(ViewPath, model);
        }
        config.MerchantId = model.MerchantId.Trim();
        config.ReferenceTemplate = string.IsNullOrWhiteSpace(model.ReferenceTemplate)
            ? ICEPAYSettings.DefaultReferenceTemplate : model.ReferenceTemplate.Trim();
        if (!string.IsNullOrWhiteSpace(model.Secret)) config.EncryptedSecret = encryption.EncryptText(model.Secret);
        await settings.SaveSettingOverridablePerStoreAsync(config, x => x.MerchantId, model.OverrideForStore, scope, false);
        await settings.SaveSettingOverridablePerStoreAsync(config, x => x.EncryptedSecret, model.OverrideForStore, scope, false);
        await settings.SaveSettingOverridablePerStoreAsync(config, x => x.ReferenceTemplate, model.ReferenceTemplate_OverrideForStore, scope, false);
        await settings.ClearCacheAsync();
        notifications.SuccessNotification(await localization.GetResourceAsync("Admin.Plugins.Saved"));
        return RedirectToAction(nameof(Configure));
    }
}
