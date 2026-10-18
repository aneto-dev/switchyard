using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed record PaymentsOutboxWorkerOptions
{
    public PaymentsOutboxWorkerOptions(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Outbox poll interval must be positive.");
        }

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
