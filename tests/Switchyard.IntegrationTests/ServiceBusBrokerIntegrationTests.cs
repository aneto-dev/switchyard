using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Messaging.ServiceBus;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class ServiceBusBrokerIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable =
        "SWITCHYARD_SERVICEBUS_E2E_CONNECTION_STRING";

    private const string TopicName = "switchyard-events";
    private const string SubscriptionName = "ordering";
    private const string InventorySubscriptionName = "inventory";
    private const string InventoryReleaseSubscriptionName = "inventory-release";
    private const string PaymentsSubscriptionName = "payments";

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task PublishReceiveAndCompletePreservesEnvelopeThroughRealBroker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = GetConnectionString();
        var envelope = new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            "inventory.event.servicebus-e2e.v1",
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
            "inventory.event.servicebus-e2e-retry.v1",
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
            Subject = "inventory.event.servicebus-e2e-invalid.v1",
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

        // The disposable emulator is torn down after verification, so the
        // terminal DLQ message does not need a second settlement operation.
    }

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task InventoryCommandIsRoutedOnlyToInventorySubscription()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var connectionString =
            GetConnectionString();

        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                "inventory.command.reserve.v1",
                """{"requestId":"00000000-0000-0000-0000-000000000001"}""",
                new DateTimeOffset(
                    2026,
                    9,
                    23,
                    20,
                    50,
                    0,
                    TimeSpan.Zero),
                Guid.NewGuid(),
                Guid.NewGuid());

        await using var client =
            new ServiceBusClient(
                connectionString);

        await using var sender =
            client.CreateSender(
                TopicName);

        await using var inventoryReceiver =
            client.CreateReceiver(
                TopicName,
                InventorySubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        await using var orderingReceiver =
            client.CreateReceiver(
                TopicName,
                SubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        var transport =
            new ServiceBusMessageTransport(
                sender);

        await transport.PublishAsync(
            envelope,
            cancellationToken);

        var inventoryDelivery =
            await ReceiveExpectedAsync(
                inventoryReceiver,
                envelope.MessageId,
                cancellationToken);

        Assert.Equal(
            envelope.MessageType,
            inventoryDelivery.Subject);

        await inventoryReceiver.CompleteMessageAsync(
            inventoryDelivery,
            cancellationToken);

        var orderingDelivery =
            await orderingReceiver.ReceiveMessageAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);

        Assert.Null(orderingDelivery);
    }

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task ReleaseInventoryCommandIsRoutedOnlyToReleaseSubscription()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var connectionString =
            GetConnectionString();

        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                "inventory.command.release.v1",
                """{"reservationId":"00000000-0000-0000-0000-000000000001"}""",
                new DateTimeOffset(
                    2026,
                    9,
                    24,
                    14,
                    55,
                    0,
                    TimeSpan.Zero),
                Guid.NewGuid(),
                Guid.NewGuid());

        await using var client =
            new ServiceBusClient(
                connectionString);

        await using var sender =
            client.CreateSender(
                TopicName);

        await using var releaseBufferReceiver =
            client.CreateReceiver(
                TopicName,
                InventoryReleaseSubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        await using var activeInventoryReceiver =
            client.CreateReceiver(
                TopicName,
                InventorySubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        var transport =
            new ServiceBusMessageTransport(
                sender);

        await transport.PublishAsync(
            envelope,
            cancellationToken);

        var releaseDelivery =
            await ReceiveExpectedAsync(
                releaseBufferReceiver,
                envelope.MessageId,
                cancellationToken);

        Assert.Equal(
            envelope.MessageType,
            releaseDelivery.Subject);

        await releaseBufferReceiver.CompleteMessageAsync(
            releaseDelivery,
            cancellationToken);

        var activeInventoryDelivery =
            await activeInventoryReceiver.ReceiveMessageAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);

        Assert.Null(activeInventoryDelivery);
    }

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task PaymentCommandIsRoutedOnlyToPaymentsSubscription()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var connectionString =
            GetConnectionString();

        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                "payments.command.authorise.v1",
                """{"requestId":"00000000-0000-0000-0000-000000000001"}""",
                new DateTimeOffset(
                    2026,
                    9,
                    24,
                    15,
                    0,
                    0,
                    TimeSpan.Zero),
                Guid.NewGuid(),
                Guid.NewGuid());

        await using var client =
            new ServiceBusClient(
                connectionString);

        await using var sender =
            client.CreateSender(
                TopicName);

        await using var paymentsReceiver =
            client.CreateReceiver(
                TopicName,
                PaymentsSubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        await using var orderingReceiver =
            client.CreateReceiver(
                TopicName,
                SubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        var transport =
            new ServiceBusMessageTransport(
                sender);

        await transport.PublishAsync(
            envelope,
            cancellationToken);

        var paymentsDelivery =
            await ReceiveExpectedAsync(
                paymentsReceiver,
                envelope.MessageId,
                cancellationToken);

        Assert.Equal(
            envelope.MessageType,
            paymentsDelivery.Subject);

        await paymentsReceiver.CompleteMessageAsync(
            paymentsDelivery,
            cancellationToken);

        var orderingDelivery =
            await orderingReceiver.ReceiveMessageAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);

        Assert.Null(orderingDelivery);
    }

    [Fact]
    [Trait("Category", "ServiceBusEmulator")]
    public async Task PaymentAuthorisationCommandConsumesAndPublishesOutcomeThroughRealBroker()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var connectionString =
            GetConnectionString();

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_servicebus_payments_e2e")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        var paymentsConnectionString =
            postgres.GetConnectionString();

        await using (var migrations =
                     CreatePaymentsDbContext(
                         paymentsConnectionString))
        {
            await migrations.Database.MigrateAsync(
                cancellationToken);
        }

        var nowUtc =
            new DateTimeOffset(
                2026,
                10,
                18,
                18,
                0,
                0,
                TimeSpan.Zero);

        var requestId =
            Guid.NewGuid();
        var orderId =
            Guid.NewGuid();

        var command =
            new AuthorisePaymentV1(
                requestId,
                orderId,
                1299.99m,
                "GBP",
                nowUtc.AddMinutes(10));

        var envelope =
            new IntegrationMessageEnvelope(
                requestId,
                AuthorisePaymentV1.MessageType,
                JsonSerializer.Serialize(
                    command,
                    JsonSerializerOptions.Web),
                nowUtc,
                orderId,
                Guid.NewGuid());

        await using var client =
            new ServiceBusClient(
                connectionString);

        await using var sender =
            client.CreateSender(
                TopicName);

        await using var paymentsReceiver =
            client.CreateReceiver(
                TopicName,
                PaymentsSubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        await using var orderingReceiver =
            client.CreateReceiver(
                TopicName,
                SubscriptionName,
                new ServiceBusReceiverOptions
                {
                    ReceiveMode =
                        ServiceBusReceiveMode.PeekLock
                });

        var transport =
            new ServiceBusMessageTransport(
                sender);

        await transport.PublishAsync(
            envelope,
            cancellationToken);

        var paymentDelivery =
            await ReceiveExpectedAsync(
                paymentsReceiver,
                envelope.MessageId,
                cancellationToken);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);

        var dbContextFactory =
            new TestPaymentsDbContextFactory(
                paymentsConnectionString);

        var timeProvider =
            new FixedTimeProvider(
                nowUtc);

        var consumer =
            new PaymentsInboundMessageConsumer(
                dbContextFactory,
                provider,
                timeProvider);

        var processor =
            new ServiceBusInboundMessageProcessor(
                consumer,
                new ServiceBusReceiveOptions(
                    PaymentsSubscriptionName,
                    retryBaseDelay: TimeSpan.Zero,
                    retryMaxDelay: TimeSpan.Zero,
                    retryJitterRatio: 0),
                timeProvider,
                NullLogger<ServiceBusInboundMessageProcessor>.Instance);

        await processor.ProcessAsync(
            new ProcessMessageEventArgs(
                paymentDelivery,
                paymentsReceiver,
                "payments-servicebus-e2e",
                cancellationToken));

        Assert.Equal(
            1,
            provider.InvocationCount);

        var dispatchCycle =
            new PaymentsOutboxDispatchCycle(
                dbContextFactory,
                transport,
                new FixedTimeProvider(
                    nowUtc.AddSeconds(1)),
                new PaymentsOutboxWorkerOptions(
                    10,
                    TimeSpan.FromMinutes(1),
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100)));

        var dispatch =
            await dispatchCycle.DispatchAsync(
                cancellationToken);

        Assert.Equal(
            1,
            dispatch.Published);

        var outcome =
            await ReceiveExpectedSubjectAsync(
                orderingReceiver,
                PaymentAuthorisedV1.MessageType,
                orderId,
                cancellationToken);

        Assert.Equal(
            orderId.ToString("D"),
            outcome.CorrelationId);

        await orderingReceiver.CompleteMessageAsync(
            outcome,
            cancellationToken);

        var paymentRedelivery =
            await paymentsReceiver.ReceiveMessageAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);

        Assert.Null(
            paymentRedelivery);
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

    private static async Task<ServiceBusReceivedMessage> ReceiveExpectedSubjectAsync(
        ServiceBusReceiver receiver,
        string messageType,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(
                20);

        var expectedCorrelationId =
            correlationId.ToString("D");

        while (DateTimeOffset.UtcNow < deadline)
        {
            var received =
                await receiver.ReceiveMessageAsync(
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

            if (received is null)
            {
                continue;
            }

            if (string.Equals(
                    received.Subject,
                    messageType,
                    StringComparison.Ordinal) &&
                string.Equals(
                    received.CorrelationId,
                    expectedCorrelationId,
                    StringComparison.Ordinal))
            {
                return received;
            }

            await receiver.CompleteMessageAsync(
                received,
                cancellationToken);
        }

        throw new TimeoutException(
            $"Service Bus subject '{messageType}' for correlation '{expectedCorrelationId}' was not received within 20 seconds.");
    }

    private static PaymentsDbContext CreatePaymentsDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        return new PaymentsDbContext(
            options);
    }

    private sealed class TestPaymentsDbContextFactory :
        IDbContextFactory<PaymentsDbContext>
    {
        private readonly string _connectionString;

        public TestPaymentsDbContextFactory(
            string connectionString)
        {
            _connectionString =
                connectionString;
        }

        public PaymentsDbContext CreateDbContext() =>
            ServiceBusBrokerIntegrationTests.CreatePaymentsDbContext(
                _connectionString);

        public Task<PaymentsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;
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
