using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Reconciliation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed partial class PaymentsAuthorisationReconciliationCycle :
    IPaymentsAuthorisationReconciliationCycle
{
    private readonly IDbContextFactory<PaymentsDbContext> _dbContextFactory;
    private readonly IPaymentAuthorisationReconciliationProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentsAuthorisationReconciliationOptions _options;
    private readonly ILogger<PaymentsAuthorisationReconciliationCycle> _logger;

    public PaymentsAuthorisationReconciliationCycle(
        IDbContextFactory<PaymentsDbContext> dbContextFactory,
        IPaymentAuthorisationReconciliationProvider provider,
        TimeProvider timeProvider,
        PaymentsAuthorisationReconciliationOptions options,
        ILogger<PaymentsAuthorisationReconciliationCycle> logger)
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

        _options =
            options ??
            throw new ArgumentNullException(nameof(options));

        _logger =
            logger ??
            throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PaymentsAuthorisationReconciliationCycleResult> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        Guid[] dueRequestIds;

        await using (var queryDbContext =
                     await _dbContextFactory.CreateDbContextAsync(
                         cancellationToken))
        {
            var queryStore =
                new EfPaymentAuthorisationReconciliationStore(
                    queryDbContext);

            dueRequestIds =
                await queryStore.GetDueRequestIdsAsync(
                    _options.BatchSize,
                    _timeProvider.GetUtcNow(),
                    _options.RetryDelay,
                    _options.MaxAttempts,
                    cancellationToken);
        }

        var applied =
            0;
        var resolved =
            0;
        var escalated =
            0;
        var failed =
            0;

        foreach (var requestId in dueRequestIds)
        {
            try
            {
                await using var dbContext =
                    await _dbContextFactory.CreateDbContextAsync(
                        cancellationToken);

                var store =
                    new EfPaymentAuthorisationReconciliationStore(
                        dbContext);

                var current =
                    await store.GetAsync(
                        requestId,
                        cancellationToken);

                if (current is null ||
                    current.Status !=
                        PaymentAuthorisationStatus.Indeterminate ||
                    current.ReconciliationAttemptCount >=
                        _options.MaxAttempts)
                {
                    continue;
                }

                var providerResult =
                    await _provider.ReconcileAsync(
                        new PaymentProviderReconciliationRequest(
                            current.ProviderIdempotencyKey,
                            current.PaymentId,
                            current.OrderId,
                            current.ProviderReference),
                        cancellationToken);

                var status =
                    providerResult.Outcome switch
                    {
                        PaymentProviderReconciliationOutcome.Authorised =>
                            PaymentAuthorisationStatus.Authorised,
                        PaymentProviderReconciliationOutcome.Declined =>
                            PaymentAuthorisationStatus.Declined,
                        PaymentProviderReconciliationOutcome.Unknown =>
                            PaymentAuthorisationStatus.Indeterminate,
                        _ =>
                            throw new InvalidOperationException(
                                "Payment provider returned an unsupported reconciliation outcome.")
                    };

                var providerReference =
                    string.IsNullOrWhiteSpace(
                        providerResult.ProviderReference)
                        ? current.ProviderReference
                        : providerResult.ProviderReference.Trim();

                if ((status is
                         PaymentAuthorisationStatus.Authorised or
                         PaymentAuthorisationStatus.Declined) &&
                    providerReference is null)
                {
                    throw new InvalidOperationException(
                        "A definite provider reconciliation outcome requires a provider reference.");
                }

                var reconciledAtUtc =
                    _timeProvider.GetUtcNow();

                var outbox =
                    new EfPaymentsOutboxStore(
                        dbContext);

                var decision =
                    await store.ApplyAtomicallyAsync(
                        new PaymentAuthorisationReconciliation(
                            requestId,
                            status,
                            providerReference,
                            current.ReconciliationAttemptCount,
                            reconciledAtUtc),
                        async (appliedDecision, token) =>
                        {
                            var envelope =
                                BuildOutcomeEnvelope(
                                    appliedDecision.State,
                                    _options.MaxAttempts);

                            if (envelope is not null)
                            {
                                await outbox.AddAsync(
                                    envelope,
                                    token);
                            }
                        },
                        cancellationToken);

                if (!decision.Applied)
                {
                    continue;
                }

                applied++;

                if (decision.State.Status is
                    PaymentAuthorisationStatus.Authorised or
                    PaymentAuthorisationStatus.Declined)
                {
                    resolved++;
                }
                else if (
                    decision.State.Status ==
                        PaymentAuthorisationStatus.Indeterminate &&
                    decision.State.ReconciliationAttemptCount >=
                        _options.MaxAttempts)
                {
                    escalated++;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;

                LogRequestFailure(
                    _logger,
                    requestId,
                    exception);
            }
        }

        return new PaymentsAuthorisationReconciliationCycleResult(
            dueRequestIds.Length,
            applied,
            resolved,
            escalated,
            failed);
    }

    private static IntegrationMessageEnvelope? BuildOutcomeEnvelope(
        PaymentAuthorisationReconciliationState state,
        int maxAttempts)
    {
        if (state.Status ==
            PaymentAuthorisationStatus.Authorised)
        {
            var providerReference =
                state.ProviderReference ??
                throw new InvalidOperationException(
                    "Authorised reconciliation requires a provider reference.");

            var resolvedAtUtc =
                state.ResolvedAtUtc ??
                throw new InvalidOperationException(
                    "Authorised reconciliation requires a resolved time.");

            return new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                PaymentAuthorisedV1.MessageType,
                JsonSerializer.Serialize(
                    new PaymentAuthorisedV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.Amount,
                        state.Currency,
                        providerReference,
                        resolvedAtUtc),
                    JsonSerializerOptions.Web),
                resolvedAtUtc,
                state.OrderId,
                state.RequestId);
        }

        if (state.Status ==
            PaymentAuthorisationStatus.Declined)
        {
            var providerReference =
                state.ProviderReference ??
                throw new InvalidOperationException(
                    "Declined reconciliation requires a provider reference.");

            var resolvedAtUtc =
                state.ResolvedAtUtc ??
                throw new InvalidOperationException(
                    "Declined reconciliation requires a resolved time.");

            return new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                PaymentAuthorisationFailedV1.MessageType,
                JsonSerializer.Serialize(
                    new PaymentAuthorisationFailedV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.Amount,
                        state.Currency,
                        PaymentAuthorisationFailedV1.DeclinedReason,
                        providerReference,
                        resolvedAtUtc,
                        ReservationValidUntilUtc: null),
                    JsonSerializerOptions.Web),
                resolvedAtUtc,
                state.OrderId,
                state.RequestId);
        }

        if (state.Status ==
                PaymentAuthorisationStatus.Indeterminate &&
            state.ReconciliationAttemptCount >=
                maxAttempts)
        {
            var observedAtUtc =
                state.LastReconciledAtUtc ??
                throw new InvalidOperationException(
                    "NeedsAttention reconciliation requires an observation time.");

            return new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                PaymentAuthorisationNeedsAttentionV1.MessageType,
                JsonSerializer.Serialize(
                    new PaymentAuthorisationNeedsAttentionV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.Amount,
                        state.Currency,
                        state.ProviderReference,
                        PaymentAuthorisationNeedsAttentionV1.ReconciliationExhaustedReason,
                        state.ReconciliationAttemptCount,
                        observedAtUtc),
                    JsonSerializerOptions.Web),
                observedAtUtc,
                state.OrderId,
                state.RequestId);
        }

        return null;
    }

    [LoggerMessage(
        EventId = 1031,
        Level = LogLevel.Error,
        Message = "Payment authorisation reconciliation failed for request {RequestId}.")]
    private static partial void LogRequestFailure(
        ILogger logger,
        Guid requestId,
        Exception exception);
}
