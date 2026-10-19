using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Plugin.Payments.ICEPAY.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;

namespace Nop.Plugin.Payments.ICEPAY.Eps;

public class EpsPaymentMethod(ICEPAYPaymentService payments, IWebHelper web,
    ILocalizationService localization, IHttpContextAccessor http, INotificationService notifications)
    : ICEPAYPaymentMethod("Eps", payments, web, localization, http, notifications);
