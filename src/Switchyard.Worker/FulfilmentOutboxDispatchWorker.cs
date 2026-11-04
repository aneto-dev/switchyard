using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class FulfilmentOutboxDispatchWorker :
    BackgroundService
{
    private readonly IFulfilmentOutboxDispatchCycle _cycle;
    private readonly FulfilmentOutboxWorkerOptions _options;
    private readonly ILogger<FulfilmentOutboxDispatchWorker> _logger;

    public FulfilmentOutboxDispatchWorker(
        IFulfilmentOutboxDispatchCycle cycle,
        FulfilmentOutboxWorkerOptions options,
        ILogger<FulfilmentOutboxDispatchWorker> logger)
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
        EventId = 1031,
        Level = LogLevel.Information,
        Message = "Fulfilment outbox dispatch claimed {Claimed}, published {Published} and failed {Failed}.")]
    private static partial void LogDispatchResult(
        ILogger logger,
        int claimed,
        int published,
        int failed);

    [LoggerMessage(
        EventId = 1032,
        Level = LogLevel.Error,
        Message = "Fulfilment outbox dispatch cycle failed.")]
    private static partial void LogDispatchFailure(
        ILogger logger,
        Exception exception);
}
