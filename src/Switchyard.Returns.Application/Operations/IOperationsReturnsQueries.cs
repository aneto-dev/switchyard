namespace Switchyard.Returns.Application.Operations;

// Read-only view of Returns' own state for the operations order view.
public interface IOperationsReturnsQueries
{
    // Null until Returns has seen a delivered shipment for the order.
    Task<OperationsReturnsView?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
}
