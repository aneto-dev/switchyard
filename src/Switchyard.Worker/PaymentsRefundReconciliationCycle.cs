using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.RefundReconciliation;
using Switchyard.Payments.Domain.Refunds;
using Switchyard.Payments.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed partial class PaymentsRefundReconciliationCycle :
    IPaymentsRefundReconciliationCycle
{
    private readonly IDbContextFactory<PaymentsDbContext> _dbContextFactory;
    private readonly IPaymentRefundReconciliationProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentsRefundReconciliationOptions _options;
    private readonly ILogger<PaymentsRefundReconciliationCycle> _logger;

    public PaymentsRefundReconciliationCycle(
        IDbContextFactory<PaymentsDbContext> dbContextFactory,
        IPaymentRefundReconciliationProvider provider,
        TimeProvider timeProvider,
        PaymentsRefundReconciliationOptions options,
        ILogger<PaymentsRefundReconciliationCycle> logger)
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

    public async Task<PaymentsRefundReconciliationCycleResult> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        Guid[] dueRequestIds;

        await using (var queryDbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken))
        {
            dueRequestIds =
                (await new EfPaymentRefundLifecycleStore(
                    queryDbContext)
                    .GetDueRequestIdsAsync(
                        _options.BatchSize,
                        _timeProvider.GetUtcNow(),
                        _options.RetryDelay,
                        _options.MaxAttempts,
                        cancellationToken))
                .ToArray();
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
                    new EfPaymentRefundLifecycleStore(
                        dbContext);

                var current =
                    await store.GetAsync(
                        requestId,
                        cancellationToken);

                if (current is null ||
                    current.Status !=
                        PaymentRefundStatus.Indeterminate ||
                    current.ReconciliationAttemptCount >=
                        _options.MaxAttempts)
                {
                    continue;
                }

                var providerResult =
                    await _provider.ReconcileAsync(
                        new PaymentProviderRefundReconciliationRequest(
                            current.ProviderIdempotencyKey,
                            current.RequestId,
                            current.PaymentId,
                            current.OrderId,
                            current.Amount,
                            current.Currency,
                            current.ProviderReference),
                        cancellationToken);

                var status =
                    providerResult.Outcome switch
                    {
                        PaymentProviderRefundReconciliationOutcome.Succeeded =>
                            PaymentRefundStatus.Succeeded,
                        PaymentProviderRefundReconciliationOutcome.NotApplied =>
                            PaymentRefundStatus.NotApplied,
                        PaymentProviderRefundReconciliationOutcome.Unknown =>
                            PaymentRefundStatus.Indeterminate,
                        _ =>
                            throw new InvalidOperationException(
                                "Payment provider returned an unsupported refund reconciliation outcome.")
                    };

                var providerReference =
                    string.IsNullOrWhiteSpace(
                        providerResult.ProviderReference)
                        ? current.ProviderReference
                        : providerResult.ProviderReference.Trim();

                var reconciledAtUtc =
                    _timeProvider.GetUtcNow();

                var outbox =
                    new EfPaymentsOutboxStore(
                        dbContext);

                var decision =
                    await store.ApplyAtomicallyAsync(
                        new PaymentRefundReconciliation(
                            requestId,
                            status,
                            providerReference,
                            reconciledAtUtc,
                            current.ReconciliationAttemptCount),
                        async (state, token) =>
                        {
                            if (state.Status is not
                                (PaymentRefundStatus.Succeeded or
                                 PaymentRefundStatus.NotApplied))
                            {
                                return;
                            }

                            var envelope =
                                PaymentsRefundOutcomeMessageFactory.Create(
                                    state);

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
                    PaymentRefundStatus.Succeeded or
                    PaymentRefundStatus.NotApplied)
                {
                    resolved++;
                }
                else if (
                    decision.State.Status ==
                        PaymentRefundStatus.Indeterminate &&
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

        return new PaymentsRefundReconciliationCycleResult(
            dueRequestIds.Length,
            applied,
            resolved,
            escalated,
            failed);
    }

    [LoggerMessage(
        EventId = 1061,
        Level = LogLevel.Error,
        Message = "Payment refund reconciliation failed for request {RequestId}.")]
    private static partial void LogRequestFailure(
        ILogger logger,
        Guid requestId,
        Exception exception);
}
