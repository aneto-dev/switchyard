namespace Switchyard.Worker;

public interface IPaymentsSettlementReconciliationCycle
{
    Task<PaymentsSettlementReconciliationCycleResult> ReconcileAsync(
        CancellationToken cancellationToken);
}

public sealed record PaymentsSettlementReconciliationCycleResult(
    int Due,
    int Applied,
    int Resolved,
    int Escalated,
    int Failed);
