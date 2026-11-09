using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfReturnsOutboxStore : IOutboxStore
{
    private readonly ReturnsDbContext _dbContext;

    public EfReturnsOutboxStore(
        ReturnsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task AddAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _dbContext.OutboxMessages.AddAsync(
            new ReturnsOutboxMessageRecord
            {
                MessageId = message.MessageId,
                MessageType = message.MessageType,
                PayloadJson = message.PayloadJson,
                OccurredAtUtc = message.OccurredAtUtc,
                CorrelationId = message.CorrelationId,
                CausationId = message.CausationId,
                AvailableAtUtc = message.OccurredAtUtc,
                DeliveryAttemptCount = 0
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimPendingAsync(
        int batchSize,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            leaseDuration,
            TimeSpan.Zero);

        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var records =
            await _dbContext.OutboxMessages
                            .FromSqlInterpolated(
                                $"""
                                SELECT *
                                FROM returns.outbox_messages
                                WHERE published_at_utc IS NULL
                                  AND available_at_utc <= {nowUtc}
                                  AND (locked_until_utc IS NULL OR locked_until_utc <= {nowUtc})
                                ORDER BY occurred_at_utc, message_id
                                LIMIT {batchSize}
                                FOR UPDATE SKIP LOCKED
                                """)
                            .ToListAsync(
                                cancellationToken);

        var lockedUntilUtc =
            nowUtc.Add(leaseDuration);

        var claimed =
            new List<ClaimedOutboxMessage>(
                records.Count);

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

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return claimed;
    }

    public async Task MarkPublishedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var record =
            await LockMessageAsync(
                messageId,
                cancellationToken);

        EnsureLease(
            record,
            messageId,
            lockToken,
            publishedAtUtc);

        ArgumentOutOfRangeException.ThrowIfLessThan(
            publishedAtUtc,
            record.OccurredAtUtc);

        record.PublishedAtUtc = publishedAtUtc;
        record.LockToken = null;
        record.LockedUntilUtc = null;
        record.LastError = null;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid messageId,
        Guid lockToken,
        DateTimeOffset failedAtUtc,
        DateTimeOffset nextAttemptAtUtc,
        string failureKind,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            nextAttemptAtUtc,
            failedAtUtc);

        if (string.IsNullOrWhiteSpace(
                failureKind))
        {
            throw new ArgumentException(
                "Failure kind is required.",
                nameof(failureKind));
        }

        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var record =
            await LockMessageAsync(
                messageId,
                cancellationToken);

        EnsureLease(
            record,
            messageId,
            lockToken,
            failedAtUtc);

        record.AvailableAtUtc =
            nextAttemptAtUtc;

        record.LockToken = null;
        record.LockedUntilUtc = null;
        record.LastError =
            failureKind.Trim().Length <= 200
                ? failureKind.Trim()
                : failureKind.Trim()[..200];

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    private async Task<ReturnsOutboxMessageRecord> LockMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message ID cannot be empty.",
                nameof(messageId));
        }

        return await _dbContext.OutboxMessages
                               .FromSqlInterpolated(
                                   $"""
                                   SELECT *
                                   FROM returns.outbox_messages
                                   WHERE message_id = {messageId}
                                   FOR UPDATE
                                   """)
                               .SingleOrDefaultAsync(
                                   cancellationToken) ??
               throw new InvalidOperationException(
                   $"Outbox message '{messageId}' does not exist.");
    }

    private static void EnsureLease(
        ReturnsOutboxMessageRecord record,
        Guid messageId,
        Guid lockToken,
        DateTimeOffset operationAtUtc)
    {
        if (lockToken == Guid.Empty ||
            record.PublishedAtUtc is not null ||
            record.LockToken != lockToken ||
            record.LockedUntilUtc is null ||
            record.LockedUntilUtc <= operationAtUtc)
        {
            throw new OutboxLeaseLostException(
                messageId);
        }
    }

    private static IntegrationMessageEnvelope ToEnvelope(
        ReturnsOutboxMessageRecord record) =>
        new(
            record.MessageId,
            record.MessageType,
            record.PayloadJson,
            record.OccurredAtUtc,
            record.CorrelationId,
            record.CausationId);
}
