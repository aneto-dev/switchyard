using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed record ReturnsOutboxWorkerOptions
{
    public ReturnsOutboxWorkerOptions(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        TimeSpan pollInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            pollInterval,
            TimeSpan.Zero);

        Dispatch =
            new OutboxDispatchOptions(
                batchSize,
                leaseDuration,
                retryDelay);

        PollInterval = pollInterval;
    }

    public OutboxDispatchOptions Dispatch { get; }

    public TimeSpan PollInterval { get; }
}
