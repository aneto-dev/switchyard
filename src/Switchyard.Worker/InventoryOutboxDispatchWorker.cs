using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class InventoryOutboxDispatchWorker :
    BackgroundService
{
    private readonly IInventoryOutboxDispatchCycle _cycle;
    private readonly InventoryOutboxWorkerOptions _options;
    private readonly ILogger<InventoryOutboxDispatchWorker> _logger;

    public InventoryOutboxDispatchWorker(
        IInventoryOutboxDispatchCycle cycle,
        InventoryOutboxWorkerOptions options,
        ILogger<InventoryOutboxDispatchWorker> logger)
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
        EventId = 1011,
        Level = LogLevel.Information,
        Message = "Inventory outbox dispatch claimed {Claimed}, published {Published} and failed {Failed}.")]
    private static partial void LogDispatchResult(
        ILogger logger,
        int claimed,
        int published,
        int failed);

    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Error,
        Message = "Inventory outbox dispatch cycle failed.")]
    private static partial void LogDispatchFailure(
        ILogger logger,
        Exception exception);
}
