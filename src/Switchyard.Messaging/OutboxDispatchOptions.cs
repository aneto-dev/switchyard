namespace Switchyard.Messaging;

public sealed record OutboxDispatchOptions
{
    public OutboxDispatchOptions(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan retryDelay)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Outbox batch size must be positive.");
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "Outbox lease duration must be positive.");
        }

        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelay),
                "Outbox retry delay cannot be negative.");
        }

        BatchSize = batchSize;
        LeaseDuration = leaseDuration;
        RetryDelay = retryDelay;
    }

    public int BatchSize { get; }

    public TimeSpan LeaseDuration { get; }

    public TimeSpan RetryDelay { get; }
}
