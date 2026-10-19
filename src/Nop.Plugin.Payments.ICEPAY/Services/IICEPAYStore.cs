using Nop.Plugin.Payments.ICEPAY.Domain;

namespace Nop.Plugin.Payments.ICEPAY.Services;

public interface IICEPAYStore
{
    Task<T> WithOrderLockAsync<T>(int orderId, Func<Task<T>> action);
    Task<ICEPAYPaymentAttempt?> FindAttemptAsync(int id);
    Task<IList<ICEPAYPaymentAttempt>> AttemptsAsync(int orderId);
    Task<IList<ICEPAYPaymentAttempt>> PendingAsync();
    Task TouchAsync(int attemptId);
    Task InsertAsync(ICEPAYPaymentAttempt attempt);
    Task SaveAsync(ICEPAYPaymentAttempt attempt);
    Task<IList<ICEPAYRefundRequest>> RefundsAsync(int orderId);
    Task InsertAsync(ICEPAYRefundRequest refund);
    Task SaveAsync(ICEPAYRefundRequest refund);
}
