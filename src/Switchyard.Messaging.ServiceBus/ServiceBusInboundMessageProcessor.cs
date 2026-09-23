using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Switchyard.Messaging.ServiceBus;

public sealed partial class ServiceBusInboundMessageProcessor
{
    private const string FailureKindProperty = "switchyardFailureKind";

    private readonly IIntegrationMessageConsumer _consumer;
    private readonly ServiceBusReceiveOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServiceBusInboundMessageProcessor> _logger;

    public ServiceBusInboundMessageProcessor(
        IIntegrationMessageConsumer consumer,
        ServiceBusReceiveOptions options,
        TimeProvider timeProvider,
        ILogger<ServiceBusInboundMessageProcessor> logger)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessAsync(ProcessMessageEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        IntegrationMessageEnvelope message;

        try
        {
            message = ServiceBusMessageEnvelopeMapper.Map(args.Message);
        }
        catch (ServiceBusMessageContractException exception)
        {
            LogInvalidEnvelope(
                _logger,
                args.Message.MessageId,
                args.Message.Subject,
                exception);

            await args.DeadLetterMessageAsync(
                args.Message,
                "InvalidEnvelope",
                "Required Service Bus envelope metadata or JSON payload is invalid.",
                args.CancellationToken);

            return;
        }

        try
        {
            await _consumer.ConsumeAsync(message, args.CancellationToken);
        }
        catch (OperationCanceledException)
            when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnsupportedIntegrationMessageException exception)
        {
            LogUnsupportedMessage(
                _logger,
                message.MessageId,
                message.MessageType,
                exception);

            await args.DeadLetterMessageAsync(
                args.Message,
                "UnsupportedMessageType",
                "The consumer does not support this integration message type.",
                args.CancellationToken);

            return;
        }
        catch (InboxMessageConflictException exception)
        {
            LogInboxConflict(
                _logger,
                message.MessageId,
                message.MessageType,
                exception);

            await args.DeadLetterMessageAsync(
                args.Message,
                "MessageIdentityConflict",
                "The message ID conflicts with an existing durable inbox receipt.",
                args.CancellationToken);

            return;
        }
        catch (NonRetryableIntegrationMessageException exception)
        {
            LogNonRetryableFailure(
                _logger,
                message.MessageId,
                message.MessageType,
                exception);

            await args.DeadLetterMessageAsync(
                args.Message,
                "NonRetryableProcessing",
                "The consumer rejected this message as non-retryable.",
                args.CancellationToken);

            return;
        }
        catch (Exception exception)
        {
            var retryDelay = CalculateRetryDelay(args.Message.DeliveryCount);

            LogRetryableFailure(
                _logger,
                message.MessageId,
                message.MessageType,
                args.Message.DeliveryCount,
                retryDelay.TotalMilliseconds,
                exception);

            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(
                    retryDelay,
                    _timeProvider,
                    args.CancellationToken);
            }

            await args.AbandonMessageAsync(
                args.Message,
                new Dictionary<string, object>
                {
                    [FailureKindProperty] = exception.GetType().Name
                },
                args.CancellationToken);

            return;
        }

        // Local inbox/state/outbox work is committed before broker settlement.
        // If completion is uncertain, the same MessageId can be safely redelivered.
        await args.CompleteMessageAsync(
            args.Message,
            args.CancellationToken);
    }

    private TimeSpan CalculateRetryDelay(int deliveryCount)
    {
        if (_options.RetryBaseDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var exponent = Math.Clamp(deliveryCount - 1, 0, 20);
        var delayMilliseconds =
            _options.RetryBaseDelay.TotalMilliseconds *
            Math.Pow(2, exponent);

        delayMilliseconds = Math.Min(
            delayMilliseconds,
            _options.RetryMaxDelay.TotalMilliseconds);

        var jitterMultiplier =
            1 +
            ((Random.Shared.NextDouble() * 2 - 1) *
             _options.RetryJitterRatio);

        return TimeSpan.FromMilliseconds(
            Math.Max(0, delayMilliseconds * jitterMultiplier));
    }

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Warning,
        Message = "Invalid Service Bus envelope {BrokerMessageId} ({Subject}) was dead-lettered.")]
    private static partial void LogInvalidEnvelope(
        ILogger logger,
        string? brokerMessageId,
        string? subject,
        Exception exception);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Warning,
        Message = "Inbound message {MessageId} ({MessageType}) was dead-lettered because its type is unsupported.")]
    private static partial void LogUnsupportedMessage(
        ILogger logger,
        Guid messageId,
        string messageType,
        Exception exception);

    [LoggerMessage(
        EventId = 1203,
        Level = LogLevel.Error,
        Message = "Inbound message {MessageId} ({MessageType}) conflicted with the durable inbox identity and was dead-lettered.")]
    private static partial void LogInboxConflict(
        ILogger logger,
        Guid messageId,
        string messageType,
        Exception exception);

    [LoggerMessage(
        EventId = 1204,
        Level = LogLevel.Warning,
        Message = "Inbound message {MessageId} ({MessageType}) failed non-retryably and was dead-lettered.")]
    private static partial void LogNonRetryableFailure(
        ILogger logger,
        Guid messageId,
        string messageType,
        Exception exception);

    [LoggerMessage(
        EventId = 1205,
        Level = LogLevel.Warning,
        Message = "Inbound message {MessageId} ({MessageType}) failed on delivery {DeliveryCount}; abandoning after {RetryDelayMilliseconds} ms.")]
    private static partial void LogRetryableFailure(
        ILogger logger,
        Guid messageId,
        string messageType,
        int deliveryCount,
        double retryDelayMilliseconds,
        Exception exception);
}
