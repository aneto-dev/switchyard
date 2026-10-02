namespace Switchyard.Ordering.Application.Placement;

public sealed record OrderPlacementRelease(
    Guid ReservationRequestId,
    Guid ReservationId);
