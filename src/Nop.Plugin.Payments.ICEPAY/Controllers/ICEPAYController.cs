using ICEPAY.Checkout;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.ICEPAY.Services;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Payments.ICEPAY.Controllers;

public class ICEPAYController(IICEPAYStore history, ICEPAYPaymentService payments,
    IOrderService orders, IWorkContext workContext, ILogger logger) : BasePaymentController
{
    [HttpPost("Plugins/ICEPAY/Webhook/{attemptId:int}")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Webhook(int attemptId)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var signature = Request.Headers[WebhookUtility.SignatureHeader].ToString();
        try
        {
            await payments.HandleWebhookAsync(attemptId, body, signature);
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (IcepayException ex) when (ex.StatusCode is null) { return Unauthorized(); }
        catch (System.Text.Json.JsonException) { return BadRequest(); }
        catch (Exception)
        {
            await logger.WarningAsync($"ICEPAY webhook attempt {attemptId} was not processed; retry is required.");
            return StatusCode(503);
        }
    }

    [HttpGet("Plugins/ICEPAY/Return/{attemptId:int}")]
    public async Task<IActionResult> Return(int attemptId)
    {
        var attempt = await history.FindAttemptAsync(attemptId);
        if (attempt is null) return NotFound();
        var order = await orders.GetOrderByIdAsync(attempt.OrderId);
        var customer = await workContext.GetCurrentCustomerAsync();
        if (order is null || order.Deleted || order.CustomerId != customer.Id) return NotFound();
        try { await payments.RefreshAsync(attemptId); }
        catch (Exception) { await logger.WarningAsync($"ICEPAY return for attempt {attemptId} could not refresh payment status."); }
        // Query-string values never determine payment status.
        return RedirectToRoute("OrderDetails", new { orderId = order.Id });
    }
}
