using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class PaymentsRefundReconciliationWorker :
    BackgroundService
{
    private readonly IPaymentsRefundReconciliationCycle _cycle;
    private readonly PaymentsRefundReconciliationOptions _options;
    private readonly ILogger<PaymentsRefundReconciliationWorker> _logger;

    public PaymentsRefundReconciliationWorker(
        IPaymentsRefundReconciliationCycle cycle,
        PaymentsRefundReconciliationOptions options,
        ILogger<PaymentsRefundReconciliationWorker> logger)
    {
        _cycle = cycle;
        _options = options;
        _logger = logger;
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
        EventId = 1062,
        Level = LogLevel.Information,
        Message = "Payment refund reconciliation found {Due} due, applied {Applied}, resolved {Resolved}, escalated {Escalated} and failed {Failed}.")]
    private static partial void LogCycleResult(
        ILogger logger,
        int due,
        int applied,
        int resolved,
        int escalated,
        int failed);

    [LoggerMessage(
        EventId = 1063,
        Level = LogLevel.Error,
        Message = "Payment refund reconciliation cycle failed.")]
    private static partial void LogCycleFailure(
        ILogger logger,
        Exception exception);
}
