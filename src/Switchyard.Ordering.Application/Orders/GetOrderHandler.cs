using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Orders;

public sealed class GetOrderHandler
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<GetOrderResult?> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return null;
        }

        var order = await _orderRepository.GetByIdAsync(new OrderId(orderId), cancellationToken);

        if (order is null)
        {
            return null;
        }

        var lines = order.Lines.Select(line => new GetOrderLineResult(
                                          line.Id.Value,
                                          line.Product.SkuCode.Value,
                                          line.Product.ProductName,
                                          line.Quantity,
                                          line.UnitPrice.Amount,
                                          line.LineTotal.Amount,
                                          line.UnitPrice.Currency))
                               .ToArray();

        return new GetOrderResult(order.Id.Value, order.OrderNumber.Value, order.Status.ToString(),
                                  order.Total.Amount, order.Total.Currency, order.CreatedAtUtc, lines);
    }
}
