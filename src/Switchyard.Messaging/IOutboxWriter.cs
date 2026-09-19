namespace Switchyard.Messaging;

public interface IOutboxWriter
{
    Task AddAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
