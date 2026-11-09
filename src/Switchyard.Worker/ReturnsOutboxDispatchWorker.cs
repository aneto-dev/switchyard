using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class ReturnsOutboxDispatchWorker :
    BackgroundService
{
    private readonly IReturnsOutboxDispatchCycle _cycle;
    private readonly ReturnsOutboxWorkerOptions _options;
    private readonly ILogger<ReturnsOutboxDispatchWorker> _logger;

    public ReturnsOutboxDispatchWorker(
        IReturnsOutboxDispatchCycle cycle,
        ReturnsOutboxWorkerOptions options,
        ILogger<ReturnsOutboxDispatchWorker> logger)
    {
        _cycle =
            cycle ??
            throw new ArgumentNullException(
                nameof(cycle));

        _options =
            options ??
            throw new ArgumentNullException(
                nameof(options));

        _logger =
            logger ??
            throw new ArgumentNullException(
                nameof(logger));
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
                    await _cycle.DispatchAsync(
                        stoppingToken);

                drainImmediately =
                    result.Claimed >=
                    _options.Dispatch.BatchSize;

                if (result.Claimed > 0)
                {
                    LogDispatchResult(
                        _logger,
                        result.Claimed,
                        result.Published,
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
                LogDispatchFailure(
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
        EventId = 1041,
        Level = LogLevel.Information,
        Message = "Returns outbox dispatch claimed {Claimed}, published {Published} and failed {Failed}.")]
    private static partial void LogDispatchResult(
        ILogger logger,
        int claimed,
        int published,
        int failed);

    [LoggerMessage(
        EventId = 1042,
        Level = LogLevel.Error,
        Message = "Returns outbox dispatch cycle failed.")]
    private static partial void LogDispatchFailure(
        ILogger logger,
        Exception exception);
}
