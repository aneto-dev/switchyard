using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Operations;

public sealed record OperationsOrderListFilter(OrderStatus? Status, string? OrderNumber, int Limit)
{
    public const int DefaultLimit = 25;

    public const int MaxLimit = 100;
}
