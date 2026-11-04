using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IFulfilmentOutboxDispatchCycle
{
    Task<OutboxDispatchResult> DispatchAsync(
        CancellationToken cancellationToken);
}
