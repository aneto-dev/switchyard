using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class PaymentsAuthorisationReconciliationWorker :
    BackgroundService
{
    private readonly IPaymentsAuthorisationReconciliationCycle _cycle;
    private readonly PaymentsAuthorisationReconciliationOptions _options;
    private readonly ILogger<PaymentsAuthorisationReconciliationWorker> _logger;

    public PaymentsAuthorisationReconciliationWorker(
        IPaymentsAuthorisationReconciliationCycle cycle,
        PaymentsAuthorisationReconciliationOptions options,
        ILogger<PaymentsAuthorisationReconciliationWorker> logger)
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
            var drainImmediately =
                false;

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
        EventId = 1032,
        Level = LogLevel.Information,
        Message = "Payments authorisation reconciliation found {Due} due, applied {Applied}, resolved {Resolved}, escalated {Escalated} and failed {Failed}.")]
    private static partial void LogCycleResult(
        ILogger logger,
        int due,
        int applied,
        int resolved,
        int escalated,
        int failed);

    [LoggerMessage(
        EventId = 1033,
        Level = LogLevel.Error,
        Message = "Payments authorisation reconciliation cycle failed.")]
    private static partial void LogCycleFailure(
        ILogger logger,
        Exception exception);
}
