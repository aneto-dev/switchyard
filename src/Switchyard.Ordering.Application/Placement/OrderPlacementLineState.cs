namespace Switchyard.Ordering.Application.Placement;

public enum OrderPlacementLineState
{
    AwaitingReservation = 0,
    Reserved = 1,
    Rejected = 2,
    AwaitingRelease = 3,
    Released = 4
}
