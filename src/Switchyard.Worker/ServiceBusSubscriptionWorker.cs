using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Switchyard.Worker;

public sealed partial class ServiceBusSubscriptionWorker :
    BackgroundService
{
    private readonly ServiceBusSubscriptionEndpoint[] _endpoints;
    private readonly ILogger<ServiceBusSubscriptionWorker> _logger;

    public ServiceBusSubscriptionWorker(
        IEnumerable<ServiceBusSubscriptionEndpoint> endpoints,
        ILogger<ServiceBusSubscriptionWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _endpoints = endpoints.ToArray();

        if (_endpoints.Length == 0)
        {
            throw new InvalidOperationException(
                "At least one Service Bus subscription endpoint is required.");
        }

        _logger =
            logger ??
            throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var started =
            new List<ServiceBusSubscriptionEndpoint>(
                _endpoints.Length);

        try
        {
            foreach (var endpoint in _endpoints)
            {
                endpoint.Processor.ProcessMessageAsync +=
                    endpoint.MessageProcessor.ProcessAsync;

                endpoint.Processor.ProcessErrorAsync +=
                    ProcessErrorAsync;

                await endpoint.Processor.StartProcessingAsync(
                    stoppingToken);

                started.Add(endpoint);

                LogEndpointStarted(
                    _logger,
                    endpoint.Name);
            }

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
            foreach (var endpoint in started.AsEnumerable().Reverse())
            {
                await endpoint.Processor.StopProcessingAsync(
                    CancellationToken.None);

                LogEndpointStopped(
                    _logger,
                    endpoint.Name);
            }

            foreach (var endpoint in _endpoints)
            {
                endpoint.Processor.ProcessMessageAsync -=
                    endpoint.MessageProcessor.ProcessAsync;

                endpoint.Processor.ProcessErrorAsync -=
                    ProcessErrorAsync;
            }
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
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Service Bus endpoint {EndpointName} started.")]
    private static partial void LogEndpointStarted(
        ILogger logger,
        string endpointName);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Error,
        Message = "Service Bus processor error from {ErrorSource} on {EntityPath}.")]
    private static partial void LogProcessorError(
        ILogger logger,
        ServiceBusErrorSource errorSource,
        string entityPath,
        Exception exception);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Information,
        Message = "Service Bus endpoint {EndpointName} stopped.")]
    private static partial void LogEndpointStopped(
        ILogger logger,
        string endpointName);
}
