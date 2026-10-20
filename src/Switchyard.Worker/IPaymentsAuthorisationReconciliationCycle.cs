namespace Switchyard.Worker;

public interface IPaymentsAuthorisationReconciliationCycle
{
    Task<PaymentsAuthorisationReconciliationCycleResult> ReconcileAsync(
        CancellationToken cancellationToken);
}
