using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Switchyard.Messaging.ServiceBus;

namespace Switchyard.Worker;

public sealed partial class ServiceBusSubscriptionWorker :
    BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ServiceBusInboundMessageProcessor _messageProcessor;
    private readonly ILogger<ServiceBusSubscriptionWorker> _logger;

    public ServiceBusSubscriptionWorker(
        ServiceBusProcessor processor,
        ServiceBusInboundMessageProcessor messageProcessor,
        ILogger<ServiceBusSubscriptionWorker> logger)
    {
        _processor =
            processor ?? throw new ArgumentNullException(nameof(processor));

        _messageProcessor =
            messageProcessor ??
            throw new ArgumentNullException(nameof(messageProcessor));

        _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync +=
            _messageProcessor.ProcessAsync;

        _processor.ProcessErrorAsync +=
            ProcessErrorAsync;

        var started = false;

        try
        {
            await _processor.StartProcessingAsync(
                stoppingToken);

            started = true;

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (started)
            {
                await _processor.StopProcessingAsync(
                    CancellationToken.None);
            }

            _processor.ProcessMessageAsync -=
                _messageProcessor.ProcessAsync;

            _processor.ProcessErrorAsync -=
                ProcessErrorAsync;
        }
    }

    private Task ProcessErrorAsync(
        ProcessErrorEventArgs args)
    {
        LogProcessorError(
            _logger,
            args.ErrorSource,
            args.EntityPath,
            args.Exception);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Error,
        Message = "Service Bus processor error from {ErrorSource} on {EntityPath}.")]
    private static partial void LogProcessorError(
        ILogger logger,
        ServiceBusErrorSource errorSource,
        string entityPath,
        Exception exception);
}
