using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IInventoryOutboxDispatchCycle
{
    Task<OutboxDispatchResult> DispatchAsync(
        CancellationToken cancellationToken);
}
