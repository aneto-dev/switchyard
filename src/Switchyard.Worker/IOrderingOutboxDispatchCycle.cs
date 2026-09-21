using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IOrderingOutboxDispatchCycle
{
    Task<OutboxDispatchResult> DispatchAsync(CancellationToken cancellationToken);
}
