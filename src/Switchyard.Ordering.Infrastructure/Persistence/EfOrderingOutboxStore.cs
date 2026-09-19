using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderingOutboxStore : IOutboxStore
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderingOutboxStore(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _dbContext.OutboxMessages.AddAsync(
            new OutboxMessageRecord
            {
                MessageId = message.MessageId,
                MessageType = message.MessageType,
                PayloadJson = message.PayloadJson,
                OccurredAtUtc = message.OccurredAtUtc,
                CorrelationId = message.CorrelationId,
                CausationId = message.CausationId,
                AvailableAtUtc = message.OccurredAtUtc,
                DeliveryAttemptCount = 0,
                LastAttemptAtUtc = null,
                LockToken = null,
                LockedUntilUtc = null,
                PublishedAtUtc = null,
                LastError = null
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimPendingAsync(
        int batchSize,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
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

        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var records = await _dbContext.OutboxMessages
                                      .FromSqlInterpolated(
                                          $"""
                                          SELECT *
                                          FROM ordering.outbox_messages
                                          WHERE published_at_utc IS NULL
                                            AND available_at_utc <= {nowUtc}
                                            AND (locked_until_utc IS NULL OR locked_until_utc <= {nowUtc})
                                          ORDER BY occurred_at_utc, message_id
                                          LIMIT {batchSize}
                                          FOR UPDATE SKIP LOCKED
                                          """)
                                      .ToListAsync(cancellationToken);

        var lockedUntilUtc = nowUtc.Add(leaseDuration);
        var claimed = new List<ClaimedOutboxMessage>(records.Count);

        foreach (var record in records)
        {
            var lockToken = Guid.NewGuid();

            record.LockToken = lockToken;
            record.LockedUntilUtc = lockedUntilUtc;
            record.DeliveryAttemptCount++;
            record.LastAttemptAtUtc = nowUtc;

            claimed.Add(
                new ClaimedOutboxMessage(
                    ToEnvelope(record),
                    lockToken,
                    record.DeliveryAttemptCount));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    public async Task MarkPublishedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be empty.", nameof(messageId));
        }

        if (lockToken == Guid.Empty)
        {
            throw new ArgumentException("Lock token cannot be empty.", nameof(lockToken));
        }

        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var record = await LockMessageAsync(messageId, cancellationToken);
        EnsureLease(record, messageId, lockToken, publishedAtUtc);

        if (publishedAtUtc < record.OccurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publishedAtUtc),
                "Published time cannot be before the message occurred.");
        }

        record.PublishedAtUtc = publishedAtUtc;
        record.LockToken = null;
        record.LockedUntilUtc = null;
        record.LastError = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset failedAtUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureKind,
        CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be empty.", nameof(messageId));
        }

        if (lockToken == Guid.Empty)
        {
            throw new ArgumentException("Lock token cannot be empty.", nameof(lockToken));
        }

        if (nextAttemptAtUtc < failedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextAttemptAtUtc),
                "Next attempt time cannot be before the failure time.");
        }

        if (string.IsNullOrWhiteSpace(failureKind))
        {
            throw new ArgumentException("Failure kind is required.", nameof(failureKind));
        }

        var normalizedFailureKind = failureKind.Trim();

        if (normalizedFailureKind.Length > 200)
        {
            normalizedFailureKind = normalizedFailureKind[..200];
        }

        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var record = await LockMessageAsync(messageId, cancellationToken);
        EnsureLease(record, messageId, lockToken, failedAtUtc);

        record.AvailableAtUtc = nextAttemptAtUtc;
        record.LockToken = null;
        record.LockedUntilUtc = null;
        record.LastError = normalizedFailureKind;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<OutboxMessageRecord> LockMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.OutboxMessages
                                     .FromSqlInterpolated(
                                         $"""
                                         SELECT *
                                         FROM ordering.outbox_messages
                                         WHERE message_id = {messageId}
                                         FOR UPDATE
                                         """)
                                     .SingleOrDefaultAsync(cancellationToken);

        return record ?? throw new InvalidOperationException(
            $"Outbox message '{messageId}' does not exist.");
    }

    private static void EnsureLease(
        OutboxMessageRecord record,
        Guid messageId,
        Guid lockToken,
        DateTimeOffset operationAtUtc)
    {
        if (record.PublishedAtUtc is not null ||
            record.LockToken != lockToken ||
            record.LockedUntilUtc is null ||
            record.LockedUntilUtc <= operationAtUtc)
        {
            throw new OutboxLeaseLostException(messageId);
        }
    }

    private static IntegrationMessageEnvelope ToEnvelope(
        OutboxMessageRecord record)
    {
        return new IntegrationMessageEnvelope(
            record.MessageId,
            record.MessageType,
            record.PayloadJson,
            record.OccurredAtUtc,
            record.CorrelationId,
            record.CausationId);
    }
}
