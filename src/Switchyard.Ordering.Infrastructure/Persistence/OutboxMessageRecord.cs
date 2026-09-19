namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OutboxMessageRecord
{
    public Guid MessageId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid? CausationId { get; set; }

    public DateTimeOffset AvailableAtUtc { get; set; }

    public int DeliveryAttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public Guid? LockToken { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public string? LastError { get; set; }
}
