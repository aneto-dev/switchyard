namespace Switchyard.Inventory.Application.Reservations;

public sealed class InventoryReservationReleaseConflictException :
    Exception
{
    public InventoryReservationReleaseConflictException(
        Guid reservationId)
        : base(
            "The inventory release command does not match the stored reservation identity.")
    {
        ReservationId = reservationId;
    }

    public Guid ReservationId { get; }
}
