using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Plugin.Payments.ICEPAY.Services;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Payments.ICEPAY;

public class ICEPAYCorePlugin(ILocalizationService localization,
    IScheduleTaskService tasks, IWebHelper web) : BasePlugin
{
    private const string TaskType = "Nop.Plugin.Payments.ICEPAY.Services.ICEPAYReconcileTask, Nop.Plugin.Payments.ICEPAY";
    public override string GetConfigurationPageUrl() => $"{web.GetStoreLocation()}Admin/ICEPAYConfiguration/Configure";

    public override async Task InstallAsync()
    {
        var resources = new Dictionary<string, string>
        {
            ["Plugins.Payments.ICEPAY.MerchantId"] = "Merchant ID",
            ["Plugins.Payments.ICEPAY.Secret"] = "Merchant secret",
            ["Plugins.Payments.ICEPAY.SecretHint"] = "Leave blank to retain the existing secret. Secrets are stored encrypted.",
            ["Plugins.Payments.ICEPAY.Override"] = "Use separate ICEPAY settings for this store",
            ["Plugins.Payments.ICEPAY.Setup"] = "Configure ICEPAY, then install and activate the desired ICEPAY payment methods. Only methods available to this merchant are shown at checkout. The store URL must use HTTPS.",
            ["Plugins.Payments.ICEPAY.RefundError"] = "The ICEPAY refund was not confirmed. Check the order notes and ICEPAY Portal before retrying.",
            ["Plugins.Payments.ICEPAY.PaymentError"] = "Your order is saved, but ICEPAY payment could not be started. Try the payment link again or contact the store.",
            ["Plugins.Payments.ICEPAY.InvalidCredentials"] = "Enter a positive numeric merchant ID and a secret. Enter a new secret when changing merchant ID."
        };
        foreach (var method in PaymentMethods.All)
            resources[$"Plugins.Payments.ICEPAY.Method.{method.Name}"] = method.Label;
        await localization.AddOrUpdateLocaleResourceAsync(resources);
        if (await tasks.GetTaskByTypeAsync(TaskType) is null)
            await tasks.InsertTaskAsync(new ScheduleTask { Name = "Refresh ICEPAY payments", Type = TaskType, Seconds = 120, Enabled = true, StopOnError = false });
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        var task = await tasks.GetTaskByTypeAsync(TaskType);
        if (task is not null) await tasks.DeleteTaskAsync(task);
        // Preserve settings and payment history for reinstalls and operational reconciliation.
        await base.UninstallAsync();
    }
}
