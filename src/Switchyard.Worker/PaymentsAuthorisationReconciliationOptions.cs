namespace Switchyard.Worker;

public sealed record PaymentsAuthorisationReconciliationOptions
{
    public PaymentsAuthorisationReconciliationOptions(
        int batchSize,
        int maxAttempts,
        TimeSpan retryDelay,
        TimeSpan pollInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxAttempts);

        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelay),
                "Reconciliation retry delay cannot be negative.");
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Reconciliation poll interval must be positive.");
        }

        BatchSize =
            batchSize;
        MaxAttempts =
            maxAttempts;
        RetryDelay =
            retryDelay;
        PollInterval =
            pollInterval;
    }

    public int BatchSize { get; }

    public int MaxAttempts { get; }

    public TimeSpan RetryDelay { get; }

    public TimeSpan PollInterval { get; }
}
