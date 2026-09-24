using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IInventoryInboundMessageRoute
{
    string MessageType { get; }

    Task HandleAsync(
        InventoryDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
