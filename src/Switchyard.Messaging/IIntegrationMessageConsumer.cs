namespace Switchyard.Messaging;

public interface IIntegrationMessageConsumer
{
    Task ConsumeAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
