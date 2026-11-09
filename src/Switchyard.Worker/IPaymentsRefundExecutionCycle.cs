namespace Switchyard.Worker;

public interface IPaymentsRefundExecutionCycle
{
    Task<PaymentsRefundExecutionCycleResult> ExecuteAsync(
        CancellationToken cancellationToken);
}

public sealed record PaymentsRefundExecutionCycleResult(
    int Due,
    int Applied,
    int Succeeded,
    int Indeterminate,
    int Failed);
