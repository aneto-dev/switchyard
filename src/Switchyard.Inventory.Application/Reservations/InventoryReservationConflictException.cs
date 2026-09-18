namespace Switchyard.Inventory.Application.Reservations;

public sealed class InventoryReservationConflictException : Exception
{
    public InventoryReservationConflictException(Guid requestId)
        : base("The inventory reservation request ID has already been used with different reservation data.")
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}
