using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class PaymentsRefundExecutionWorker :
    BackgroundService
{
    private readonly IPaymentsRefundExecutionCycle _cycle;
    private readonly PaymentsRefundExecutionOptions _options;
    private readonly ILogger<PaymentsRefundExecutionWorker> _logger;

    public PaymentsRefundExecutionWorker(
        IPaymentsRefundExecutionCycle cycle,
        PaymentsRefundExecutionOptions options,
        ILogger<PaymentsRefundExecutionWorker> logger)
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
                    await _cycle.ExecuteAsync(
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
                        result.Succeeded,
                        result.Indeterminate,
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
        EventId = 1052,
        Level = LogLevel.Information,
        Message = "Payment refund execution found {Due} due, applied {Applied}, succeeded {Succeeded}, indeterminate {Indeterminate} and failed {Failed}.")]
    private static partial void LogCycleResult(
        ILogger logger,
        int due,
        int applied,
        int succeeded,
        int indeterminate,
        int failed);

    [LoggerMessage(
        EventId = 1053,
        Level = LogLevel.Error,
        Message = "Payment refund execution cycle failed.")]
    private static partial void LogCycleFailure(
        ILogger logger,
        Exception exception);
}
