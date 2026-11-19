namespace Switchyard.Ordering.Application.Operations;

// Read-only queries for the operations order view. They read Ordering's own tables directly
// rather than rehydrating aggregates, because the view needs placement state the Order
// aggregate does not own.
public interface IOperationsOrderQueries
{
    Task<IReadOnlyList<OperationsOrderSummary>> ListRecentAsync(
        OperationsOrderListFilter filter, CancellationToken cancellationToken);

    Task<OperationsOrderDetail?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}
