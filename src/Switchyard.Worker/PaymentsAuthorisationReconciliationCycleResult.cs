namespace Switchyard.Worker;

public sealed record PaymentsAuthorisationReconciliationCycleResult(
    int Due,
    int Applied,
    int Resolved,
    int Escalated,
    int Failed);
