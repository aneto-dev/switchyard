using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;

namespace Switchyard.Payments.Infrastructure.Persistence;

public enum PaymentsInboxClaimStatus
{
    Claimed = 0,
    AlreadyProcessed = 1,
    Busy = 2
}

public sealed record PaymentsInboxClaimResult(
    PaymentsInboxClaimStatus Status,
    Guid? ProcessingToken,
    bool Recovered);

public sealed class PaymentsInboxLeaseLostException : Exception
{
    public PaymentsInboxLeaseLostException(Guid messageId)
        : base($"Payments inbox lease for message '{messageId}' is no longer owned by this processor.")
    {
        MessageId = messageId;
    }

    public Guid MessageId { get; }
}

public sealed class EfPaymentsInboxMessageProcessor
{
    private const int MaxConsumerNameLength = 200;

    private readonly PaymentsDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EfPaymentsInboxMessageProcessor(
        PaymentsDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PaymentsInboxClaimResult> ClaimAsync(
        string consumerName,
        IntegrationMessageEnvelope message,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var normalizedConsumerName = NormalizeConsumerName(consumerName);
        ArgumentNullException.ThrowIfNull(message);

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "Payments inbox processing lease must be positive.");
        }

        var payloadHash = ComputePayloadHash(message.PayloadJson);
        var receivedAtUtc = _timeProvider.GetUtcNow();
        var processingToken = Guid.NewGuid();
        var processingLockedUntilUtc = receivedAtUtc.Add(leaseDuration);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO payments.inbox_messages
            (
                consumer_name,
                message_id,
                message_type,
                payload_hash,
                occurred_at_utc,
                correlation_id,
                causation_id,
                received_at_utc,
                processed_at_utc,
                processing_token,
                processing_locked_until_utc
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
                NULL,
                {processingToken},
                {processingLockedUntilUtc}
            )
            ON CONFLICT (consumer_name, message_id) DO NOTHING;
            """,
            cancellationToken);

        if (inserted == 1)
        {
            await transaction.CommitAsync(cancellationToken);

            return new PaymentsInboxClaimResult(
                PaymentsInboxClaimStatus.Claimed,
                processingToken,
                Recovered: false);
        }

        var existing = await _dbContext.InboxMessages
                                       .FromSqlInterpolated(
                                           $"""
                                           SELECT *
                                           FROM payments.inbox_messages
                                           WHERE consumer_name = {normalizedConsumerName}
                                             AND message_id = {message.MessageId}
                                           FOR UPDATE
                                           """)
                                       .SingleAsync(cancellationToken);

        EnsureConsistent(
            existing,
            normalizedConsumerName,
            message,
            payloadHash);

        if (existing.ProcessedAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);

            return new PaymentsInboxClaimResult(
                PaymentsInboxClaimStatus.AlreadyProcessed,
                ProcessingToken: null,
                Recovered: false);
        }

        if (existing.ProcessingToken is not null &&
            existing.ProcessingLockedUntilUtc.HasValue &&
            existing.ProcessingLockedUntilUtc.Value > receivedAtUtc)
        {
            await transaction.CommitAsync(cancellationToken);

            return new PaymentsInboxClaimResult(
                PaymentsInboxClaimStatus.Busy,
                ProcessingToken: null,
                Recovered: false);
        }

        existing.ProcessingToken = processingToken;
        existing.ProcessingLockedUntilUtc = processingLockedUntilUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PaymentsInboxClaimResult(
            PaymentsInboxClaimStatus.Claimed,
            processingToken,
            Recovered: true);
    }

    public async Task<InboxProcessingResult> CompleteAsync(
        string consumerName,
        IntegrationMessageEnvelope message,
        Guid processingToken,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var normalizedConsumerName = NormalizeConsumerName(consumerName);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);

        if (processingToken == Guid.Empty)
        {
            throw new ArgumentException(
                "Payments inbox processing token cannot be empty.",
                nameof(processingToken));
        }

        var payloadHash = ComputePayloadHash(message.PayloadJson);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.InboxMessages
                                       .FromSqlInterpolated(
                                           $"""
                                           SELECT *
                                           FROM payments.inbox_messages
                                           WHERE consumer_name = {normalizedConsumerName}
                                             AND message_id = {message.MessageId}
                                           FOR UPDATE
                                           """)
                                       .SingleAsync(cancellationToken);

        EnsureConsistent(
            existing,
            normalizedConsumerName,
            message,
            payloadHash);

        if (existing.ProcessedAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);

            return new InboxProcessingResult(
                Replayed: true,
                existing.ProcessedAtUtc.Value);
        }

        if (existing.ProcessingToken != processingToken)
        {
            throw new PaymentsInboxLeaseLostException(
                message.MessageId);
        }

        await handler(cancellationToken);

        var processedAtUtc = _timeProvider.GetUtcNow();

        if (processedAtUtc < existing.ReceivedAtUtc)
        {
            processedAtUtc = existing.ReceivedAtUtc;
        }

        existing.ProcessedAtUtc = processedAtUtc;
        existing.ProcessingToken = null;
        existing.ProcessingLockedUntilUtc = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
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
        PaymentInboxMessageRecord existing,
        string consumerName,
        IntegrationMessageEnvelope message,
        string payloadHash)
    {
        if (!string.Equals(
                existing.MessageType,
                message.MessageType,
                StringComparison.Ordinal) ||
            !string.Equals(
                existing.PayloadHash,
                payloadHash,
                StringComparison.Ordinal) ||
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