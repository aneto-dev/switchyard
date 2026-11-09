namespace Switchyard.Worker;

public sealed record PaymentsRefundExecutionOptions
{
    public PaymentsRefundExecutionOptions(
        int batchSize,
        TimeSpan pollInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            pollInterval,
            TimeSpan.Zero);

        BatchSize = batchSize;
        PollInterval = pollInterval;
    }

    public int BatchSize { get; }
    public TimeSpan PollInterval { get; }
}
