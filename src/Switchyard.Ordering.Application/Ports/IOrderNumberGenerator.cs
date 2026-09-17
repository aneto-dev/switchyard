using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Ports;

public interface IOrderNumberGenerator
{
    Task<OrderNumber> NextAsync(CancellationToken cancellationToken);
}
