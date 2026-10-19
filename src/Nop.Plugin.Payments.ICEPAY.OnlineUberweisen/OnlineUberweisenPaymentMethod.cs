using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Plugin.Payments.ICEPAY.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;

namespace Nop.Plugin.Payments.ICEPAY.OnlineUberweisen;

public class OnlineUberweisenPaymentMethod(ICEPAYPaymentService payments, IWebHelper web,
    ILocalizationService localization, IHttpContextAccessor http, INotificationService notifications)
    : ICEPAYPaymentMethod("OnlineUberweisen", payments, web, localization, http, notifications);
