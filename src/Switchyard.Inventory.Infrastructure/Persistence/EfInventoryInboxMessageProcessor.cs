using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;

namespace Switchyard.Inventory.Infrastructure.Persistence;

public sealed class EfInventoryInboxMessageProcessor : IInboxMessageProcessor
{
    private const int MaxConsumerNameLength = 200;

    private readonly InventoryDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EfInventoryInboxMessageProcessor(
        InventoryDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<InboxProcessingResult> ProcessAsync(
        string consumerName,
        IntegrationMessageEnvelope message,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var normalizedConsumerName = NormalizeConsumerName(consumerName);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);

        var payloadHash = ComputePayloadHash(message.PayloadJson);
        var receivedAtUtc = _timeProvider.GetUtcNow();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO inventory.inbox_messages
            (
                consumer_name,
                message_id,
                message_type,
                payload_hash,
                occurred_at_utc,
                correlation_id,
                causation_id,
                received_at_utc,
                processed_at_utc
            )
            VALUES
            (
                {normalizedConsumerName},
                {message.MessageId},
                {message.MessageType},
                {payloadHash},
                {message.OccurredAtUtc},
                {message.CorrelationId},
                {message.CausationId},
                {receivedAtUtc},
                {receivedAtUtc}
            )
            ON CONFLICT (consumer_name, message_id) DO NOTHING;
            """,
            cancellationToken);

        if (inserted == 0)
        {
            var existing = await _dbContext.InboxMessages
                                           .AsNoTracking()
                                           .SingleAsync(
                                               record =>
                                                   record.ConsumerName == normalizedConsumerName &&
                                                   record.MessageId == message.MessageId,
                                               cancellationToken);

            EnsureConsistent(existing, normalizedConsumerName, message, payloadHash);

            await transaction.CommitAsync(cancellationToken);

            return new InboxProcessingResult(
                Replayed: true,
                existing.ProcessedAtUtc);
        }

        await handler(cancellationToken);

        // Any Inventory state or outbox messages staged by the handler are committed
        // with the inbox marker so a successful local effect cannot lose its receipt.
        await _dbContext.SaveChangesAsync(cancellationToken);

        var processedAtUtc = _timeProvider.GetUtcNow();

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE inventory.inbox_messages
            SET processed_at_utc = {processedAtUtc}
            WHERE consumer_name = {normalizedConsumerName}
              AND message_id = {message.MessageId};
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new InboxProcessingResult(
            Replayed: false,
            processedAtUtc);
    }

    private static string NormalizeConsumerName(string consumerName)
    {
        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException(
                "Consumer name is required.",
                nameof(consumerName));
        }

        var normalized = consumerName.Trim();

        if (normalized.Length > MaxConsumerNameLength)
        {
            throw new ArgumentException(
                $"Consumer name cannot exceed {MaxConsumerNameLength} characters.",
                nameof(consumerName));
        }

        return normalized;
    }

    private static string ComputePayloadHash(string payloadJson)
    {
        var bytes = Encoding.UTF8.GetBytes(payloadJson);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    private static void EnsureConsistent(
        InventoryInboxMessageRecord existing,
        string consumerName,
        IntegrationMessageEnvelope message,
        string payloadHash)
    {
        if (!string.Equals(existing.MessageType, message.MessageType, StringComparison.Ordinal) ||
            !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal) ||
            existing.OccurredAtUtc != message.OccurredAtUtc ||
            existing.CorrelationId != message.CorrelationId ||
            existing.CausationId != message.CausationId)
        {
            throw new InboxMessageConflictException(
                consumerName,
                message.MessageId);
        }
    }
}
