using Azure.Messaging.ServiceBus;
using Switchyard.Messaging.ServiceBus;
using Xunit;

namespace Switchyard.Messaging.Tests;

public sealed class ServiceBusMessageTransportTests
{
    [Fact]
    public async Task MapsIntegrationEnvelopeToServiceBusMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sender = new RecordingServiceBusSender();
        var transport = new ServiceBusMessageTransport(sender);
        var occurredAtUtc = new DateTimeOffset(2026, 9, 20, 14, 0, 0, TimeSpan.Zero);
        var causationId = Guid.NewGuid();
        var envelope = new IntegrationMessageEnvelope(
            Guid.NewGuid(), "ordering.order-accepted.v1", """{"orderId":"ORD-001"}""",
            occurredAtUtc, Guid.NewGuid(), causationId);

        await transport.PublishAsync(envelope, cancellationToken);

        var sent = Assert.IsType<ServiceBusMessage>(sender.SentMessage);
        Assert.Equal(envelope.MessageId.ToString("D"), sent.MessageId);
        Assert.Equal(envelope.MessageType, sent.Subject);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal(envelope.CorrelationId.ToString("D"), sent.CorrelationId);
        Assert.Equal(envelope.PayloadJson, sent.Body.ToString());
        Assert.Equal(occurredAtUtc.ToString("O"), sent.ApplicationProperties["occurredAtUtc"]);
        Assert.Equal(causationId.ToString("D"), sent.ApplicationProperties["causationId"]);
        Assert.Equal(cancellationToken, sender.CancellationToken);
    }

    [Fact]
    public async Task OmitsCausationPropertyWhenEnvelopeHasNoCausationId()
    {
        var sender = new RecordingServiceBusSender();
        var transport = new ServiceBusMessageTransport(sender);
        var envelope = new IntegrationMessageEnvelope(
            Guid.NewGuid(), "ordering.order-accepted.v1", """{"orderId":"ORD-002"}""",
            DateTimeOffset.UtcNow, Guid.NewGuid(), null);

        await transport.PublishAsync(envelope, TestContext.Current.CancellationToken);
        var sent = Assert.IsType<ServiceBusMessage>(sender.SentMessage);
        Assert.DoesNotContain("causationId", sent.ApplicationProperties.Keys);
    }

    [Fact]
    public void TransportOptionsRequireExactlyOneCredentialSource()
    {
        Assert.Throws<ArgumentException>(() =>
            new ServiceBusTransportOptions("switchyard-events", null, null));
        Assert.Throws<ArgumentException>(() =>
            new ServiceBusTransportOptions(
                "switchyard-events", "Endpoint=sb://localhost/;", "switchyard.servicebus.windows.net"));
    }

    private sealed class RecordingServiceBusSender : ServiceBusSender
    {
        public ServiceBusMessage? SentMessage { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            SentMessage = message;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
