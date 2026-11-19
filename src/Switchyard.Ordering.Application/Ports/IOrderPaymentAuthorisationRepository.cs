using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Ports;

public interface IOrderPaymentAuthorisationRepository
{
    // Callers hold the placement process row lock, which serialises writes per order.
    Task<OrderPaymentAuthorisation?> GetByOrderIdAsync(OrderId orderId, CancellationToken cancellationToken);

    Task AddAsync(OrderPaymentAuthorisation authorisation, CancellationToken cancellationToken);

    Task UpdateAsync(OrderPaymentAuthorisation authorisation, CancellationToken cancellationToken);
}
