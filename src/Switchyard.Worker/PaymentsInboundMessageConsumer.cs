using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;
using Switchyard.Payments.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class PaymentsInboundMessageConsumer :
    IIntegrationMessageConsumer
{
    private const string ConsumerName = "payments-authorisation";
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<PaymentsDbContext> _dbContextFactory;
    private readonly IPaymentAuthorisationProvider _provider;
    private readonly TimeProvider _timeProvider;

    public PaymentsInboundMessageConsumer(
        IDbContextFactory<PaymentsDbContext> dbContextFactory,
        IPaymentAuthorisationProvider provider,
        TimeProvider timeProvider)
    {
        _dbContextFactory =
            dbContextFactory ??
            throw new ArgumentNullException(nameof(dbContextFactory));

        _provider =
            provider ??
            throw new ArgumentNullException(nameof(provider));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task ConsumeAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!string.Equals(
                message.MessageType,
                AuthorisePaymentV1.MessageType,
                StringComparison.Ordinal))
        {
            throw new UnsupportedIntegrationMessageException(
                message.MessageType);
        }

        AuthorisePaymentV1 command;

        try
        {
            command =
                JsonSerializer.Deserialize<AuthorisePaymentV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Authorise payment payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Authorise payment payload is invalid: {exception.Message}");
        }

        var amount = ValidateEnvelope(message, command);

        PaymentsInboxClaimResult claim;

        await using (var claimDbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken))
        {
            var inbox =
                new EfPaymentsInboxMessageProcessor(
                    claimDbContext,
                    _timeProvider);

            claim =
                await inbox.ClaimAsync(
                    ConsumerName,
                    message,
                    ProcessingLease,
                    cancellationToken);
        }

        if (claim.Status == PaymentsInboxClaimStatus.AlreadyProcessed)
        {
            return;
        }

        if (claim.Status == PaymentsInboxClaimStatus.Busy)
        {
            throw new InvalidOperationException(
                $"Payments inbox message '{message.MessageId}' is already being processed.");
        }

        var processingToken =
            claim.ProcessingToken ??
            throw new InvalidOperationException(
                "A claimed Payments inbox message requires a processing token.");

        var outgoing = await ResolveOutcomeAsync(
            message,
            command,
            amount,
            cancellationToken);

        await using var messagingDbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var completion =
            new EfPaymentsInboxMessageProcessor(
                messagingDbContext,
                _timeProvider);

        await completion.CompleteAsync(
            ConsumerName,
            message,
            processingToken,
            async token =>
            {
                var outbox =
                    new EfPaymentsOutboxStore(
                        messagingDbContext);

                await outbox.AddAsync(
                    outgoing,
                    token);
            },
            cancellationToken);
    }

    private async Task<IntegrationMessageEnvelope> ResolveOutcomeAsync(
        IntegrationMessageEnvelope message,
        AuthorisePaymentV1 command,
        PaymentAmount amount,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var store =
            new EfPaymentAuthorisationStore(
                dbContext);

        var existing =
            await store.FindByRequestIdAsync(
                command.RequestId,
                cancellationToken);

        if (existing is not null)
        {
            EnsureExistingMatches(
                existing,
                command,
                amount);

            if (existing.Attempt.Status !=
                PaymentAuthorisationStatus.AuthorisationPending)
            {
                return BuildOutcome(
                    ToResult(existing),
                    message,
                    command.ReservationValidUntilUtc);
            }
        }

        var nowUtc = _timeProvider.GetUtcNow();

        if (!command.ReservationValidUntilUtc.HasValue)
        {
            if (existing is not null)
            {
                return await ResolvePendingAsIndeterminateAsync(
                    store,
                    existing,
                    message,
                    command.ReservationValidUntilUtc,
                    nowUtc,
                    cancellationToken);
            }

            return BuildPreProviderFailure(
                message,
                command,
                amount,
                PaymentAuthorisationFailedV1.ReservationValidityMissingReason,
                nowUtc);
        }

        if (command.ReservationValidUntilUtc.Value <= nowUtc)
        {
            if (existing is not null)
            {
                return await ResolvePendingAsIndeterminateAsync(
                    store,
                    existing,
                    message,
                    command.ReservationValidUntilUtc,
                    nowUtc,
                    cancellationToken);
            }

            return BuildPreProviderFailure(
                message,
                command,
                amount,
                PaymentAuthorisationFailedV1.ReservationExpiredReason,
                nowUtc);
        }

        var handler =
            new AuthorisePaymentHandler(
                store,
                _provider,
                _timeProvider);

        AuthorisePaymentResult result;

        try
        {
            result =
                await handler.HandleAsync(
                    new AuthorisePaymentCommand(
                        command.RequestId,
                        command.OrderId,
                        amount.Amount,
                        amount.Currency),
                    cancellationToken);
        }
        catch (PaymentAuthorisationConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }

        return BuildOutcome(
            result,
            message,
            command.ReservationValidUntilUtc);
    }

    private static PaymentAmount ValidateEnvelope(
        IntegrationMessageEnvelope message,
        AuthorisePaymentV1 command)
    {
        if (command.RequestId == Guid.Empty ||
            command.OrderId == Guid.Empty)
        {
            throw new NonRetryableIntegrationMessageException(
                "Authorise payment command contains invalid required identifiers.");
        }

        if (message.CorrelationId != command.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Authorise payment correlation ID must match the order ID.");
        }

        try
        {
            return new PaymentAmount(
                command.Amount,
                command.Currency);
        }
        catch (ArgumentException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Authorise payment amount or currency is invalid: {exception.Message}");
        }
    }

    private static void EnsureExistingMatches(
        PaymentAuthorisationDecision existing,
        AuthorisePaymentV1 command,
        PaymentAmount amount)
    {
        if (existing.Intent.OrderId != command.OrderId ||
            existing.Intent.Amount.Amount != amount.Amount ||
            !string.Equals(
                existing.Intent.Amount.Currency,
                amount.Currency,
                StringComparison.Ordinal))
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment authorisation request identity conflicts with previously persisted logical work.");
        }
    }

    private static async Task<IntegrationMessageEnvelope> ResolvePendingAsIndeterminateAsync(
        EfPaymentAuthorisationStore store,
        PaymentAuthorisationDecision existing,
        IntegrationMessageEnvelope message,
        DateTimeOffset? reservationValidUntilUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var completed =
            await store.CompleteAsync(
                new PaymentAuthorisationCompletion(
                    existing.Attempt.RequestId,
                    PaymentAuthorisationStatus.Indeterminate,
                    ProviderReference: null,
                    nowUtc),
                cancellationToken);

        return BuildOutcome(
            ToResult(completed),
            message,
            reservationValidUntilUtc);
    }

    private static IntegrationMessageEnvelope BuildPreProviderFailure(
        IntegrationMessageEnvelope message,
        AuthorisePaymentV1 command,
        PaymentAmount amount,
        string reason,
        DateTimeOffset failedAtUtc)
    {
        var payload =
            new PaymentAuthorisationFailedV1(
                command.RequestId,
                command.OrderId,
                PaymentId: null,
                amount.Amount,
                amount.Currency,
                reason,
                ProviderReference: null,
                failedAtUtc,
                command.ReservationValidUntilUtc);

        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            PaymentAuthorisationFailedV1.MessageType,
            JsonSerializer.Serialize(
                payload,
                JsonSerializerOptions.Web),
            failedAtUtc,
            command.OrderId,
            message.MessageId);
    }

    private static IntegrationMessageEnvelope BuildOutcome(
        AuthorisePaymentResult result,
        IntegrationMessageEnvelope message,
        DateTimeOffset? reservationValidUntilUtc)
    {
        var resolvedAtUtc =
            result.ResolvedAtUtc ??
            throw new InvalidOperationException(
                "A terminal payment authorisation result requires a resolved time.");

        return result.Status switch
        {
            PaymentAuthorisationStatus.Authorised
                when !string.IsNullOrWhiteSpace(result.ProviderReference) =>
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    PaymentAuthorisedV1.MessageType,
                    JsonSerializer.Serialize(
                        new PaymentAuthorisedV1(
                            result.RequestId,
                            result.OrderId,
                            result.PaymentId,
                            result.Amount,
                            result.Currency,
                            result.ProviderReference!,
                            resolvedAtUtc),
                        JsonSerializerOptions.Web),
                    resolvedAtUtc,
                    result.OrderId,
                    message.MessageId),

            PaymentAuthorisationStatus.Declined
                when !string.IsNullOrWhiteSpace(result.ProviderReference) =>
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    PaymentAuthorisationFailedV1.MessageType,
                    JsonSerializer.Serialize(
                        new PaymentAuthorisationFailedV1(
                            result.RequestId,
                            result.OrderId,
                            result.PaymentId,
                            result.Amount,
                            result.Currency,
                            PaymentAuthorisationFailedV1.DeclinedReason,
                            result.ProviderReference,
                            resolvedAtUtc,
                            reservationValidUntilUtc),
                        JsonSerializerOptions.Web),
                    resolvedAtUtc,
                    result.OrderId,
                    message.MessageId),

            PaymentAuthorisationStatus.Indeterminate =>
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    PaymentAuthorisationIndeterminateV1.MessageType,
                    JsonSerializer.Serialize(
                        new PaymentAuthorisationIndeterminateV1(
                            result.RequestId,
                            result.OrderId,
                            result.PaymentId,
                            result.Amount,
                            result.Currency,
                            result.ProviderReference,
                            resolvedAtUtc),
                        JsonSerializerOptions.Web),
                    resolvedAtUtc,
                    result.OrderId,
                    message.MessageId),

            _ => throw new InvalidOperationException(
                "Payment authorisation returned an invalid messaging outcome.")
        };
    }

    private static AuthorisePaymentResult ToResult(
        PaymentAuthorisationDecision decision)
    {
        return new AuthorisePaymentResult(
            decision.Attempt.RequestId,
            decision.Intent.Id.Value,
            decision.Intent.OrderId,
            decision.Intent.Amount.Amount,
            decision.Intent.Amount.Currency,
            decision.Attempt.Status,
            decision.Attempt.ProviderIdempotencyKey,
            decision.Attempt.ProviderReference,
            decision.Attempt.RequestedAtUtc,
            decision.Attempt.ResolvedAtUtc,
            decision.Replayed);
    }
}
