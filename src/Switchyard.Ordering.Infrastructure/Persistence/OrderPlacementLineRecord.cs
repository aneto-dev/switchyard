using Switchyard.Ordering.Application.Placement;

namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OrderPlacementLineRecord
{
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public Guid ReservationRequestId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public OrderPlacementLineState State { get; set; }
    public Guid? ReservationId { get; set; }
}
