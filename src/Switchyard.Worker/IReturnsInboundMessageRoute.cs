using Switchyard.Messaging;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public interface IReturnsInboundMessageRoute
{
    string MessageType { get; }

    Task HandleAsync(
        ReturnsDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
