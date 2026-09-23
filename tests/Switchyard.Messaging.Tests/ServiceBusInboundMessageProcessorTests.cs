using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using Switchyard.Messaging.ServiceBus;
using Xunit;

namespace Switchyard.Messaging.Tests;

public sealed class ServiceBusInboundMessageProcessorTests
{
    [Fact]
    public async Task CompletesValidMessageAfterConsumerSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var occurredAtUtc = new DateTimeOffset(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
        var causationId = Guid.NewGuid();
        var received = CreateMessage(occurredAtUtc, causationId: causationId);
        IntegrationMessageEnvelope? consumed = null;
        var consumer = new DelegateConsumer(
            (message, _) =>
            {
                consumed = message;
                return Task.CompletedTask;
            });
        var receiver = new RecordingServiceBusReceiver();
        var processor = CreateProcessor(consumer);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(received, receiver, "test", cancellationToken));

        Assert.Equal(1, receiver.Completed);
        Assert.Equal(0, receiver.Abandoned);
        Assert.Equal(0, receiver.DeadLettered);

        var envelope = Assert.IsType<IntegrationMessageEnvelope>(consumed);
        Assert.Equal(Guid.Parse(received.MessageId), envelope.MessageId);
        Assert.Equal(received.Subject, envelope.MessageType);
        Assert.Equal(received.Body.ToString(), envelope.PayloadJson);
        Assert.Equal(occurredAtUtc, envelope.OccurredAtUtc);
        Assert.Equal(Guid.Parse(received.CorrelationId), envelope.CorrelationId);
        Assert.Equal(causationId, envelope.CausationId);
    }

    [Fact]
    public async Task DeadLettersMalformedEnvelopeWithoutCallingConsumer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var consumerCalls = 0;
        var consumer = new DelegateConsumer(
            (_, _) =>
            {
                consumerCalls++;
                return Task.CompletedTask;
            });
        var receiver = new RecordingServiceBusReceiver();
        var processor = CreateProcessor(consumer);
        var malformed = CreateMessage(
            new DateTimeOffset(2026, 9, 21, 18, 5, 0, TimeSpan.Zero),
            messageId: "not-a-guid");

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(malformed, receiver, "test", cancellationToken));

        Assert.Equal(0, consumerCalls);
        Assert.Equal(0, receiver.Completed);
        Assert.Equal(0, receiver.Abandoned);
        Assert.Equal(1, receiver.DeadLettered);
        Assert.Equal("InvalidEnvelope", receiver.DeadLetterReason);
    }

    [Fact]
    public async Task DeadLettersUnsupportedMessageType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var received = CreateMessage(
            new DateTimeOffset(2026, 9, 21, 18, 10, 0, TimeSpan.Zero));
        var consumer = new DelegateConsumer(
            (message, _) => throw new UnsupportedIntegrationMessageException(message.MessageType));
        var receiver = new RecordingServiceBusReceiver();
        var processor = CreateProcessor(consumer);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(received, receiver, "test", cancellationToken));

        Assert.Equal(1, receiver.DeadLettered);
        Assert.Equal("UnsupportedMessageType", receiver.DeadLetterReason);
    }

    [Fact]
    public async Task DeadLettersConflictingInboxIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var received = CreateMessage(
            new DateTimeOffset(2026, 9, 21, 18, 15, 0, TimeSpan.Zero));
        var consumer = new DelegateConsumer(
            (message, _) => throw new InboxMessageConflictException(
                "ordering-placement", message.MessageId));
        var receiver = new RecordingServiceBusReceiver();
        var processor = CreateProcessor(consumer);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(received, receiver, "test", cancellationToken));

        Assert.Equal(1, receiver.DeadLettered);
        Assert.Equal("MessageIdentityConflict", receiver.DeadLetterReason);
    }

    [Fact]
    public async Task DeadLettersNonRetryableConsumerFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var received = CreateMessage(
            new DateTimeOffset(2026, 9, 21, 18, 20, 0, TimeSpan.Zero));
        var consumer = new DelegateConsumer(
            (_, _) => throw new NonRetryableIntegrationMessageException("Permanent test failure."));
        var receiver = new RecordingServiceBusReceiver();
        var processor = CreateProcessor(consumer);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(received, receiver, "test", cancellationToken));

        Assert.Equal(1, receiver.DeadLettered);
        Assert.Equal("NonRetryableProcessing", receiver.DeadLetterReason);
    }

    [Fact]
    public async Task AbandonsRetryableFailureAndRecordsSafeFailureKind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var received = CreateMessage(
            new DateTimeOffset(2026, 9, 21, 18, 25, 0, TimeSpan.Zero),
            deliveryCount: 2);
        var consumer = new DelegateConsumer(
            (_, _) => throw new TimeoutException("Simulated transient failure."));
        var receiver = new RecordingServiceBusReceiver();
        var processor = CreateProcessor(consumer);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(received, receiver, "test", cancellationToken));

        Assert.Equal(0, receiver.Completed);
        Assert.Equal(1, receiver.Abandoned);
        Assert.Equal(0, receiver.DeadLettered);
        Assert.Equal(
            nameof(TimeoutException),
            receiver.AbandonedProperties["switchyardFailureKind"]);
    }

    private static ServiceBusInboundMessageProcessor CreateProcessor(
        IIntegrationMessageConsumer consumer) =>
        new(
            consumer,
            new ServiceBusReceiveOptions(
                "ordering",
                retryBaseDelay: TimeSpan.Zero,
                retryMaxDelay: TimeSpan.Zero,
                retryJitterRatio: 0),
            TimeProvider.System,
            NullLogger<ServiceBusInboundMessageProcessor>.Instance);

    private static ServiceBusReceivedMessage CreateMessage(
        DateTimeOffset occurredAtUtc,
        string? messageId = null,
        Guid? causationId = null,
        int deliveryCount = 1)
    {
        var properties = new Dictionary<string, object>
        {
            ["occurredAtUtc"] = occurredAtUtc.ToString("O")
        };

        if (causationId is Guid value)
        {
            properties["causationId"] = value.ToString("D");
        }

        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"reservationId":"RES-001"}"""),
            messageId: messageId ?? Guid.NewGuid().ToString("D"),
            correlationId: Guid.NewGuid().ToString("D"),
            subject: "inventory.reservation-confirmed.v1",
            contentType: "application/json",
            properties: properties,
            deliveryCount: deliveryCount,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private sealed class DelegateConsumer : IIntegrationMessageConsumer
    {
        private readonly Func<IntegrationMessageEnvelope, CancellationToken, Task> _handler;

        public DelegateConsumer(
            Func<IntegrationMessageEnvelope, CancellationToken, Task> handler)
        {
            _handler = handler;
        }

        public Task ConsumeAsync(
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken) =>
            _handler(message, cancellationToken);
    }

    private sealed class RecordingServiceBusReceiver : ServiceBusReceiver
    {
        public int Completed { get; private set; }
        public int Abandoned { get; private set; }
        public int DeadLettered { get; private set; }
        public string? DeadLetterReason { get; private set; }
        public IDictionary<string, object> AbandonedProperties { get; private set; } =
            new Dictionary<string, object>();

        public override Task CompleteMessageAsync(
            ServiceBusReceivedMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completed++;
            return Task.CompletedTask;
        }

        public override Task AbandonMessageAsync(
            ServiceBusReceivedMessage message,
            IDictionary<string, object>? propertiesToModify = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Abandoned++;
            AbandonedProperties = propertiesToModify ?? new Dictionary<string, object>();
            return Task.CompletedTask;
        }

        public override Task DeadLetterMessageAsync(
            ServiceBusReceivedMessage message,
            string deadLetterReason,
            string? deadLetterErrorDescription = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeadLettered++;
            DeadLetterReason = deadLetterReason;
            return Task.CompletedTask;
        }
    }
}
