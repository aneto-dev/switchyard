using Switchyard.Messaging;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public interface IOrderingInboundMessageRoute
{
    string MessageType { get; }

    Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken);
}
