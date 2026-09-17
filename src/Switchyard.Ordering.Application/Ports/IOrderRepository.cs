using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Ports;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken);
}
