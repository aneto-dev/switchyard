using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.SettlementReconciliation;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed partial class PaymentsSettlementReconciliationCycle :
    IPaymentsSettlementReconciliationCycle
{
    private readonly IDbContextFactory<PaymentsDbContext> _dbContextFactory;
    private readonly IPaymentSettlementReconciliationProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentsSettlementReconciliationOptions _options;
    private readonly ILogger<PaymentsSettlementReconciliationCycle> _logger;

    public PaymentsSettlementReconciliationCycle(
        IDbContextFactory<PaymentsDbContext> dbContextFactory,
        IPaymentSettlementReconciliationProvider provider,
        TimeProvider timeProvider,
        PaymentsSettlementReconciliationOptions options,
        ILogger<PaymentsSettlementReconciliationCycle> logger)
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

    public async Task<PaymentsSettlementReconciliationCycleResult> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        Guid[] dueRequestIds;

        await using (var queryDbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken))
        {
            dueRequestIds =
                await new EfPaymentSettlementReconciliationStore(
                    queryDbContext)
                    .GetDueCaptureRequestIdsAsync(
                        _options.BatchSize,
                        _timeProvider.GetUtcNow(),
                        _options.RetryDelay,
                        _options.MaxAttempts,
                        cancellationToken);
        }

        var applied = 0;
        var resolved = 0;
        var escalated = 0;
        var failed = 0;

        foreach (var requestId in dueRequestIds)
        {
            try
            {
                await using var dbContext =
                    await _dbContextFactory.CreateDbContextAsync(
                        cancellationToken);

                var store =
                    new EfPaymentSettlementReconciliationStore(
                        dbContext);

                var current =
                    await store.GetAsync(
                        requestId,
                        cancellationToken);

                if (current is null ||
                    current.Action !=
                        PaymentSettlementAction.Capture ||
                    current.Status !=
                        PaymentSettlementStatus.Indeterminate ||
                    current.ReconciliationAttemptCount >=
                        _options.MaxAttempts)
                {
                    continue;
                }

                var providerResult =
                    await _provider.ReconcileAsync(
                        new PaymentProviderSettlementReconciliationRequest(
                            current.ProviderIdempotencyKey,
                            current.PaymentId,
                            current.OrderId,
                            current.Action,
                            current.Amount,
                            current.Currency,
                            current.AuthorisationProviderReference,
                            current.ProviderReference),
                        cancellationToken);

                var status =
                    providerResult.Outcome switch
                    {
                        PaymentProviderSettlementReconciliationOutcome.Succeeded =>
                            PaymentSettlementStatus.Succeeded,
                        PaymentProviderSettlementReconciliationOutcome.NotApplied =>
                            PaymentSettlementStatus.NotApplied,
                        PaymentProviderSettlementReconciliationOutcome.Unknown =>
                            PaymentSettlementStatus.Indeterminate,
                        _ =>
                            throw new InvalidOperationException(
                                "Payment provider returned an unsupported settlement reconciliation outcome.")
                    };

                var providerReference =
                    string.IsNullOrWhiteSpace(
                        providerResult.ProviderReference)
                        ? current.ProviderReference
                        : providerResult.ProviderReference.Trim();

                if (status ==
                        PaymentSettlementStatus.Succeeded &&
                    string.IsNullOrWhiteSpace(
                        providerReference))
                {
                    throw new InvalidOperationException(
                        "Successful capture reconciliation requires a provider reference.");
                }

                var reconciledAtUtc =
                    _timeProvider.GetUtcNow();

                var outbox =
                    new EfPaymentsOutboxStore(
                        dbContext);

                var decision =
                    await store.ApplyAtomicallyAsync(
                        new PaymentSettlementReconciliation(
                            requestId,
                            status,
                            providerReference,
                            reconciledAtUtc,
                            current.ReconciliationAttemptCount),
                        async (appliedDecision, token) =>
                        {
                            var envelope =
                                BuildOutcomeEnvelope(
                                    appliedDecision.State);

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
                    PaymentSettlementStatus.Succeeded or
                    PaymentSettlementStatus.NotApplied)
                {
                    resolved++;
                }
                else if (
                    decision.State.Status ==
                        PaymentSettlementStatus.Indeterminate &&
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

        return new PaymentsSettlementReconciliationCycleResult(
            dueRequestIds.Length,
            applied,
            resolved,
            escalated,
            failed);
    }

    private static IntegrationMessageEnvelope? BuildOutcomeEnvelope(
        PaymentSettlementReconciliationState state)
    {
        if (state.Action !=
            PaymentSettlementAction.Capture)
        {
            return null;
        }

        if (state.Status ==
            PaymentSettlementStatus.Succeeded)
        {
            var providerReference =
                state.ProviderReference ??
                throw new InvalidOperationException(
                    "Successful capture reconciliation requires a provider reference.");

            var resolvedAtUtc =
                state.ResolvedAtUtc ??
                throw new InvalidOperationException(
                    "Successful capture reconciliation requires a resolved time.");

            return new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                PaymentCapturedV1.MessageType,
                JsonSerializer.Serialize(
                    new PaymentCapturedV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        providerReference,
                        resolvedAtUtc),
                    JsonSerializerOptions.Web),
                resolvedAtUtc,
                state.OrderId,
                state.RequestId);
        }

        if (state.Status ==
            PaymentSettlementStatus.NotApplied)
        {
            var resolvedAtUtc =
                state.ResolvedAtUtc ??
                throw new InvalidOperationException(
                    "Not-applied capture reconciliation requires a resolved time.");

            return new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                PaymentCaptureNotAppliedV1.MessageType,
                JsonSerializer.Serialize(
                    new PaymentCaptureNotAppliedV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.ProviderReference,
                        resolvedAtUtc),
                    JsonSerializerOptions.Web),
                resolvedAtUtc,
                state.OrderId,
                state.RequestId);
        }

        return null;
    }

    [LoggerMessage(
        EventId = 1041,
        Level = LogLevel.Error,
        Message = "Payment capture reconciliation failed for request {RequestId}.")]
    private static partial void LogRequestFailure(
        ILogger logger,
        Guid requestId,
        Exception exception);
}
