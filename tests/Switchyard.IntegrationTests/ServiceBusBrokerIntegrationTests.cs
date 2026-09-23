using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using Switchyard.Messaging;
using Switchyard.Messaging.ServiceBus;

namespace Switchyard.IntegrationTests;

public sealed class ServiceBusBrokerIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable =
        "SWITCHYARD_SERVICEBUS_E2E_CONNECTION_STRING";

    private const string TopicName = "switchyard-events";
    private const string SubscriptionName = "ordering";

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task PublishReceiveAndCompletePreservesEnvelopeThroughRealBroker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = GetConnectionString();
        var envelope = new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            "inventory.servicebus-e2e.v1",
            """{"reservationId":"RES-E2E-001"}""",
            new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            Guid.NewGuid());

        await using var client = new ServiceBusClient(connectionString);
        await using var sender = client.CreateSender(TopicName);
        await using var receiver = client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        var transport = new ServiceBusMessageTransport(sender);
        await transport.PublishAsync(envelope, cancellationToken);

        var received = await ReceiveExpectedAsync(
            receiver,
            envelope.MessageId,
            cancellationToken);

        IntegrationMessageEnvelope? consumed = null;
        var consumer = new DelegateConsumer(
            (message, _) =>
            {
                consumed = message;
                return Task.CompletedTask;
            });

        var processor = CreateProcessor(consumer);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(
                received,
                receiver,
                "servicebus-e2e",
                cancellationToken));

        var consumedEnvelope =
            Assert.IsType<IntegrationMessageEnvelope>(consumed);

        Assert.Equal(envelope, consumedEnvelope);

        var redelivery = await receiver.ReceiveMessageAsync(
            TimeSpan.FromSeconds(2),
            cancellationToken);

        Assert.Null(redelivery);
    }

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task RetryableFailureAbandonsAndRedeliversThroughRealBroker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = GetConnectionString();
        var envelope = new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            "inventory.servicebus-e2e-retry.v1",
            """{"reservationId":"RES-E2E-RETRY"}""",
            new DateTimeOffset(2026, 9, 23, 8, 5, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            Guid.NewGuid());

        await using var client = new ServiceBusClient(connectionString);
        await using var sender = client.CreateSender(TopicName);
        await using var receiver = client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        var transport = new ServiceBusMessageTransport(sender);
        await transport.PublishAsync(envelope, cancellationToken);

        var firstDelivery = await ReceiveExpectedAsync(
            receiver,
            envelope.MessageId,
            cancellationToken);

        var failingProcessor = CreateProcessor(
            new DelegateConsumer(
                (_, _) => throw new TimeoutException(
                    "Deterministic broker E2E retry failure.")));

        await failingProcessor.ProcessAsync(
            new ProcessMessageEventArgs(
                firstDelivery,
                receiver,
                "servicebus-e2e",
                cancellationToken));

        var redelivery = await ReceiveExpectedAsync(
            receiver,
            envelope.MessageId,
            cancellationToken);

        Assert.True(
            redelivery.DeliveryCount >= 2,
            $"Expected DeliveryCount >= 2 but found {redelivery.DeliveryCount}.");

        var completingProcessor = CreateProcessor(
            new DelegateConsumer(
                (_, _) => Task.CompletedTask));

        await completingProcessor.ProcessAsync(
            new ProcessMessageEventArgs(
                redelivery,
                receiver,
                "servicebus-e2e",
                cancellationToken));
    }

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task InvalidEnvelopeIsDeadLetteredThroughRealBroker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = GetConnectionString();
        var invalidMessageId = $"invalid-{Guid.NewGuid():N}";

        await using var client = new ServiceBusClient(connectionString);
        await using var sender = client.CreateSender(TopicName);
        await using var receiver = client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
        await using var deadLetterReceiver = client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter
            });

        var message = new ServiceBusMessage(
            BinaryData.FromString(
                """{"reservationId":"RES-E2E-DLQ"}"""))
        {
            MessageId = invalidMessageId,
            Subject = "inventory.servicebus-e2e-invalid.v1",
            ContentType = "application/json",
            CorrelationId = Guid.NewGuid().ToString("D")
        };

        message.ApplicationProperties["occurredAtUtc"] =
            new DateTimeOffset(
                2026,
                9,
                23,
                8,
                10,
                0,
                TimeSpan.Zero).ToString("O");

        await sender.SendMessageAsync(message, cancellationToken);

        var received = await ReceiveExpectedAsync(
            receiver,
            invalidMessageId,
            cancellationToken);

        var processor = CreateProcessor(
            new DelegateConsumer(
                (_, _) => Task.CompletedTask));

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(
                received,
                receiver,
                "servicebus-e2e",
                cancellationToken));

        var deadLettered = await ReceiveExpectedAsync(
            deadLetterReceiver,
            invalidMessageId,
            cancellationToken);

        Assert.Equal(
            "InvalidEnvelope",
            deadLettered.DeadLetterReason);

        await deadLetterReceiver.CompleteMessageAsync(
            deadLettered,
            cancellationToken);
    }

    private static ServiceBusInboundMessageProcessor CreateProcessor(
        IIntegrationMessageConsumer consumer) =>
        new(
            consumer,
            new ServiceBusReceiveOptions(
                SubscriptionName,
                retryBaseDelay: TimeSpan.Zero,
                retryMaxDelay: TimeSpan.Zero,
                retryJitterRatio: 0),
            TimeProvider.System,
            NullLogger<ServiceBusInboundMessageProcessor>.Instance);

    private static string GetConnectionString()
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip(
                $"Set {ConnectionStringEnvironmentVariable} to run the real Service Bus emulator integration tests.");

            return string.Empty;
        }

        return connectionString;
    }

    private static async Task<ServiceBusReceivedMessage> ReceiveExpectedAsync(
        ServiceBusReceiver receiver,
        Guid messageId,
        CancellationToken cancellationToken) =>
        await ReceiveExpectedAsync(
            receiver,
            messageId.ToString("D"),
            cancellationToken);

    private static async Task<ServiceBusReceivedMessage> ReceiveExpectedAsync(
        ServiceBusReceiver receiver,
        string messageId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var received = await receiver.ReceiveMessageAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);

            if (received is null)
            {
                continue;
            }

            if (string.Equals(
                    received.MessageId,
                    messageId,
                    StringComparison.Ordinal))
            {
                return received;
            }

            await receiver.CompleteMessageAsync(
                received,
                cancellationToken);
        }

        throw new TimeoutException(
            $"Service Bus message '{messageId}' was not received within 20 seconds.");
    }

    private sealed class DelegateConsumer : IIntegrationMessageConsumer
    {
        private readonly Func<
            IntegrationMessageEnvelope,
            CancellationToken,
            Task> _handler;

        public DelegateConsumer(
            Func<
                IntegrationMessageEnvelope,
                CancellationToken,
                Task> handler)
        {
            _handler = handler;
        }

        public Task ConsumeAsync(
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken) =>
            _handler(message, cancellationToken);
    }
}
