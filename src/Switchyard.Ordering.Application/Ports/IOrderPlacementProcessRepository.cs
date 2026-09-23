using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Ports;

public interface IOrderPlacementProcessRepository
{
    Task AddAsync(
        OrderPlacementProcess process,
        CancellationToken cancellationToken);

    Task<OrderPlacementProcess?> GetByOrderIdAsync(
        OrderId orderId,
        CancellationToken cancellationToken);
}
