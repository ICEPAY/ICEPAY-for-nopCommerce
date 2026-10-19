using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Payments.ICEPAY.Services;

public class ICEPAYReconcileTask(IICEPAYStore history, ICEPAYPaymentService payments, ILogger logger) : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        foreach (var attempt in await history.PendingAsync())
        {
            try { await payments.RefreshAsync(attempt.Id); }
            catch (Exception)
            {
                // Rotate failed items too, so one merchant outage cannot starve other stores.
                await history.TouchAsync(attempt.Id);
                await logger.WarningAsync($"ICEPAY reconciliation could not refresh attempt {attempt.Id}.");
            }
        }
    }
}
