using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IFulfilmentInboundMessageRoute
{
    string MessageType { get; }

    Task HandleAsync(
        FulfilmentDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
