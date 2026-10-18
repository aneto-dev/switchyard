using Switchyard.Messaging;

namespace Switchyard.Worker;

public interface IPaymentsOutboxDispatchCycle
{
    Task<OutboxDispatchResult> DispatchAsync(
        CancellationToken cancellationToken);
}