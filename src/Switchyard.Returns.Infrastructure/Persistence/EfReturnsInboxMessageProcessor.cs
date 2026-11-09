using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfReturnsInboxMessageProcessor : IInboxMessageProcessor
{
    private const int MaxConsumerNameLength = 200;

    private readonly ReturnsDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EfReturnsInboxMessageProcessor(
        ReturnsDbContext dbContext,
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

        var payloadHash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(message.PayloadJson)));

        var receivedAtUtc = _timeProvider.GetUtcNow();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var inserted =
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO returns.inbox_messages
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
            var existing =
                await _dbContext.InboxMessages
                                .AsNoTracking()
                                .SingleAsync(
                                    record =>
                                        record.ConsumerName == normalizedConsumerName &&
                                        record.MessageId == message.MessageId,
                                    cancellationToken);

            if (!string.Equals(existing.MessageType, message.MessageType, StringComparison.Ordinal) ||
                !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal) ||
                existing.OccurredAtUtc != message.OccurredAtUtc ||
                existing.CorrelationId != message.CorrelationId ||
                existing.CausationId != message.CausationId)
            {
                throw new InboxMessageConflictException(
                    normalizedConsumerName,
                    message.MessageId);
            }

            await transaction.CommitAsync(cancellationToken);

            return new InboxProcessingResult(
                Replayed: true,
                existing.ProcessedAtUtc);
        }

        await handler(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var processedAtUtc = _timeProvider.GetUtcNow();

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE returns.inbox_messages
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
            throw new ArgumentException("Consumer name is required.", nameof(consumerName));
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
}
