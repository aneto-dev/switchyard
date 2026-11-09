using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IReturnsOutboxDispatchCycle
{
    Task<OutboxDispatchResult> DispatchAsync(
        CancellationToken cancellationToken);
}
