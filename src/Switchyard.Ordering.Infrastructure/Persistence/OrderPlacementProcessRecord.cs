using Switchyard.Ordering.Application.Placement;

namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OrderPlacementProcessRecord
{
    public Guid OrderId { get; set; }
    public OrderPlacementState State { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<OrderPlacementLineRecord> Lines { get; } = [];
}
