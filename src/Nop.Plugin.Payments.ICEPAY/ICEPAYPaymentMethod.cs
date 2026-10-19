using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.ICEPAY.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nop.Plugin.Payments.ICEPAY;

public abstract class ICEPAYPaymentMethod(string name, ICEPAYPaymentService payments,
    IWebHelper web, ILocalizationService localization, IHttpContextAccessor http,
    INotificationService notifications) : BasePlugin, IPaymentMethod
{
    private readonly PaymentMethodDefinition _method = PaymentMethods.Get(name);
    public bool SupportCapture => false;
    public bool SupportPartiallyRefund => true;
    public bool SupportRefund => true;
    public bool SupportVoid => false;
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;
    public bool SkipPaymentInfo => true;
    public override string GetConfigurationPageUrl() => $"{web.GetStoreLocation()}Admin/ICEPAYConfiguration/Configure";
    public Task<string> GetPaymentMethodDescriptionAsync() => localization.GetResourceAsync($"Plugins.Payments.ICEPAY.Method.{name}");
    public Type GetPublicViewComponent() => typeof(ICEPAYPaymentInfoViewComponent);
    public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart) => !await payments.IsAvailableAsync(_method.ApiType);
    public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart) => Task.FromResult(0m);
    public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest request) => Task.FromResult(new ProcessPaymentResult());
    public async Task PostProcessPaymentAsync(PostProcessPaymentRequest request)
    {
        try
        {
            var url = await payments.StartAsync(request.Order, _method.ApiType);
            http.HttpContext!.Response.Redirect(url ?? $"{web.GetStoreLocation()}orderdetails/{request.Order.Id}");
        }
        catch (Exception)
        {
            // The order exists already; preserve it and provide nopCommerce's normal retry entry point.
            notifications.ErrorNotification(await localization.GetResourceAsync("Plugins.Payments.ICEPAY.PaymentError"));
            http.HttpContext!.Response.Redirect($"{web.GetStoreLocation()}orderdetails/{request.Order.Id}");
        }
    }
    public Task<bool> CanRePostProcessPaymentAsync(Order order) => Task.FromResult(!order.Deleted &&
        order.OrderStatus != OrderStatus.Cancelled && order.PaymentStatus == PaymentStatus.Pending);
    public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request)
    {
        try { return await payments.RefundAsync(request); }
        catch (Exception)
        {
            return new RefundPaymentResult { Errors = [await localization.GetResourceAsync("Plugins.Payments.ICEPAY.RefundError")] };
        }
    }
    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form) => Task.FromResult<IList<string>>([]);
    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form) => Task.FromResult(new ProcessPaymentRequest());
    public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest request) => Task.FromResult(new CapturePaymentResult { Errors = ["Capture is not supported."] });
    public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest request) => Task.FromResult(new VoidPaymentResult { Errors = ["Void is not supported."] });
    public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest request) => Task.FromResult(new ProcessPaymentResult { Errors = ["Recurring payments are not supported."] });
    public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest request) => Task.FromResult(new CancelRecurringPaymentResult { Errors = ["Recurring payments are not supported."] });
}

public class ICEPAYPaymentInfoViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => Content("");
}
