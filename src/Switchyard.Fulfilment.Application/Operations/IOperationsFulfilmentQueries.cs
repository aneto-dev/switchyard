namespace Switchyard.Fulfilment.Application.Operations;

// Read-only view of Fulfilment's own state for the operations order view.
public interface IOperationsFulfilmentQueries
{
    // Null until Fulfilment has accepted the confirmed order.
    Task<OperationsFulfilmentView?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
}
