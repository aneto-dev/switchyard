namespace Switchyard.Worker;

public interface IPaymentsRefundReconciliationCycle
{
    Task<PaymentsRefundReconciliationCycleResult> ReconcileAsync(
        CancellationToken cancellationToken);
}

public sealed record PaymentsRefundReconciliationCycleResult(
    int Due,
    int Applied,
    int Resolved,
    int Escalated,
    int Failed);
