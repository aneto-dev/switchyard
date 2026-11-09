using Switchyard.Payments.Application.RefundExecution;
using Switchyard.Payments.Application.RefundReconciliation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentRefundReconciliationStore
{
    Task<IReadOnlyList<Guid>> GetDueRequestIdsAsync(
        int batchSize,
        DateTimeOffset nowUtc,
        TimeSpan retryDelay,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task<PaymentRefundExecutionState?> GetAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<PaymentRefundReconciliationDecision> ApplyAtomicallyAsync(
        PaymentRefundReconciliation reconciliation,
        Func<PaymentRefundExecutionState, CancellationToken, Task>? onApplied,
        CancellationToken cancellationToken);
}
