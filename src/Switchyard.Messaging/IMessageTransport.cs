namespace Switchyard.Messaging;

public interface IMessageTransport
{
    Task PublishAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
