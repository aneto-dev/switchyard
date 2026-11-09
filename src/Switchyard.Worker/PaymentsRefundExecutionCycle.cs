using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.RefundExecution;
using Switchyard.Payments.Domain.Refunds;
using Switchyard.Payments.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed partial class PaymentsRefundExecutionCycle :
    IPaymentsRefundExecutionCycle
{
    private readonly IDbContextFactory<PaymentsDbContext> _dbContextFactory;
    private readonly IPaymentRefundProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentsRefundExecutionOptions _options;
    private readonly ILogger<PaymentsRefundExecutionCycle> _logger;

    public PaymentsRefundExecutionCycle(
        IDbContextFactory<PaymentsDbContext> dbContextFactory,
        IPaymentRefundProvider provider,
        TimeProvider timeProvider,
        PaymentsRefundExecutionOptions options,
        ILogger<PaymentsRefundExecutionCycle> logger)
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

    public async Task<PaymentsRefundExecutionCycleResult> ExecuteAsync(
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
                    .GetPendingRequestIdsAsync(
                        _options.BatchSize,
                        cancellationToken))
                .ToArray();
        }

        var applied = 0;
        var succeeded = 0;
        var indeterminate = 0;
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

                var outbox =
                    new EfPaymentsOutboxStore(
                        dbContext);

                var handler =
                    new ExecutePaymentRefundHandler(
                        store,
                        _provider,
                        _timeProvider);

                var decision =
                    await handler.HandleAsync(
                        requestId,
                        async (state, token) =>
                        {
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

                if (decision.State.Status ==
                    PaymentRefundStatus.Succeeded)
                {
                    succeeded++;
                }
                else if (
                    decision.State.Status ==
                    PaymentRefundStatus.Indeterminate)
                {
                    indeterminate++;
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

        return new PaymentsRefundExecutionCycleResult(
            dueRequestIds.Length,
            applied,
            succeeded,
            indeterminate,
            failed);
    }

    [LoggerMessage(
        EventId = 1051,
        Level = LogLevel.Error,
        Message = "Payment refund execution failed for request {RequestId}.")]
    private static partial void LogRequestFailure(
        ILogger logger,
        Guid requestId,
        Exception exception);
}
