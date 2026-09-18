using Switchyard.Ordering.Application.Orders;

namespace Switchyard.Ordering.Application.Ports;

public interface IOrderRequestRepository
{
    Task<AcceptedOrderRequest?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken);

    Task AddAsync(AcceptedOrderRequest request, CancellationToken cancellationToken);
}
