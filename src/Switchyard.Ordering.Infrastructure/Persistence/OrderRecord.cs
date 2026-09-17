using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OrderRecord
{
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public OrderStatus Status { get; set; }

    public List<OrderLineRecord> Lines { get; } = [];
}
