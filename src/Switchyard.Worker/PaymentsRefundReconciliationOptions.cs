namespace Switchyard.Worker;

public sealed record PaymentsRefundReconciliationOptions
{
    public PaymentsRefundReconciliationOptions(
        int batchSize,
        int maxAttempts,
        TimeSpan retryDelay,
        TimeSpan pollInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxAttempts);

        ArgumentOutOfRangeException.ThrowIfLessThan(
            retryDelay,
            TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            pollInterval,
            TimeSpan.Zero);

        BatchSize = batchSize;
        MaxAttempts = maxAttempts;
        RetryDelay = retryDelay;
        PollInterval = pollInterval;
    }

    public int BatchSize { get; }
    public int MaxAttempts { get; }
    public TimeSpan RetryDelay { get; }
    public TimeSpan PollInterval { get; }
}
