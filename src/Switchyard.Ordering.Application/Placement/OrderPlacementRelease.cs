using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

public sealed record OrderPlacementRelease(
    OrderLineId OrderLineId,
    Guid ReservationId);
