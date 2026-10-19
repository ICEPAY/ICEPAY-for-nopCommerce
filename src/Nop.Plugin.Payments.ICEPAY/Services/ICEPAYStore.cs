using LinqToDB;
using Nop.Data;
using Nop.Plugin.Payments.ICEPAY.Domain;

namespace Nop.Plugin.Payments.ICEPAY.Services;

public class ICEPAYStore(IRepository<ICEPAYPaymentAttempt> attempts,
    IRepository<ICEPAYRefundRequest> refunds, IRepository<ICEPAYOrderLock> locks) : IICEPAYStore
{
    public async Task<T> WithOrderLockAsync<T>(int orderId, Func<Task<T>> action)
    {
        if (!await locks.Table.AnyAsync(x => x.OrderId == orderId))
        {
            try { await locks.InsertAsync(new ICEPAYOrderLock { OrderId = orderId }, false); }
            catch
            {
                // A competing request may have created the unique row first.
                if (!await locks.Table.AnyAsync(x => x.OrderId == orderId)) throw;
            }
        }
        var owner = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var changed = await locks.Table.Where(x => x.OrderId == orderId && x.ExpiresOnUtc < now)
            .Set(x => x.Owner, owner).Set(x => x.ExpiresOnUtc, now.AddMinutes(5)).UpdateAsync();
        if (changed != 1) throw new InvalidOperationException("Another ICEPAY operation is in progress. Please try again shortly.");
        try { return await action(); }
        finally
        {
            await locks.Table.Where(x => x.OrderId == orderId && x.Owner == owner)
                .Set(x => x.Owner, "").Set(x => x.ExpiresOnUtc, DateTime.UnixEpoch).UpdateAsync();
        }
    }

    public async Task<ICEPAYPaymentAttempt?> FindAttemptAsync(int id) => await attempts.GetByIdAsync(id);
    public Task<IList<ICEPAYPaymentAttempt>> AttemptsAsync(int orderId) => attempts.GetAllAsync(q => q.Where(x => x.OrderId == orderId).OrderBy(x => x.Id));
    public Task<IList<ICEPAYPaymentAttempt>> PendingAsync() => attempts.GetAllAsync(q => q
        .Where(x => !x.Applied && !x.ReviewRequired && x.PaymentKey != "")
        .OrderBy(x => x.LastCheckedOnUtc).Take(50));
    public async Task TouchAsync(int attemptId) => await attempts.Table.Where(x => x.Id == attemptId)
        .Set(x => x.LastCheckedOnUtc, DateTime.UtcNow).UpdateAsync();
    public Task InsertAsync(ICEPAYPaymentAttempt attempt) => attempts.InsertAsync(attempt, false);
    public Task SaveAsync(ICEPAYPaymentAttempt attempt) => attempts.UpdateAsync(attempt, false);
    public Task<IList<ICEPAYRefundRequest>> RefundsAsync(int orderId) => refunds.GetAllAsync(q => q.Where(x => x.OrderId == orderId));
    public Task InsertAsync(ICEPAYRefundRequest refund) => refunds.InsertAsync(refund, false);
    public Task SaveAsync(ICEPAYRefundRequest refund) => refunds.UpdateAsync(refund, false);
}
