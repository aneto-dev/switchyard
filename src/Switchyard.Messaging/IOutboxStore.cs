namespace Switchyard.Messaging;

public interface IOutboxStore : IOutboxWriter
{
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimPendingAsync(
        int batchSize,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset failedAtUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureKind,
        CancellationToken cancellationToken);
}
