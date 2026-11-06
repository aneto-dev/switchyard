using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class PaymentsSettlementReconciliationWorker :
    BackgroundService
{
    private readonly IPaymentsSettlementReconciliationCycle _cycle;
    private readonly PaymentsSettlementReconciliationOptions _options;
    private readonly ILogger<PaymentsSettlementReconciliationWorker> _logger;

    public PaymentsSettlementReconciliationWorker(
        IPaymentsSettlementReconciliationCycle cycle,
        PaymentsSettlementReconciliationOptions options,
        ILogger<PaymentsSettlementReconciliationWorker> logger)
    {
        _cycle =
            cycle ??
            throw new ArgumentNullException(nameof(cycle));

        _options =
            options ??
            throw new ArgumentNullException(nameof(options));

        _logger =
            logger ??
            throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var drainImmediately = false;

            try
            {
                var result =
                    await _cycle.ReconcileAsync(
                        stoppingToken);

                drainImmediately =
                    result.Due >=
                        _options.BatchSize &&
                    result.Failed == 0;

                if (result.Due > 0 ||
                    result.Failed > 0)
                {
                    LogCycleResult(
                        _logger,
                        result.Due,
                        result.Applied,
                        result.Resolved,
                        result.Escalated,
                        result.Failed);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogCycleFailure(
                    _logger,
                    exception);
            }

            if (!drainImmediately)
            {
                await Task.Delay(
                    _options.PollInterval,
                    stoppingToken);
            }
        }
    }

    [LoggerMessage(
        EventId = 1042,
        Level = LogLevel.Information,
        Message = "Payment capture reconciliation found {Due} due, applied {Applied}, resolved {Resolved}, escalated {Escalated} and failed {Failed}.")]
    private static partial void LogCycleResult(
        ILogger logger,
        int due,
        int applied,
        int resolved,
        int escalated,
        int failed);

    [LoggerMessage(
        EventId = 1043,
        Level = LogLevel.Error,
        Message = "Payment capture reconciliation cycle failed.")]
    private static partial void LogCycleFailure(
        ILogger logger,
        Exception exception);
}
