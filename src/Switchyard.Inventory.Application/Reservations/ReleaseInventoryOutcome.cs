namespace Switchyard.Inventory.Application.Reservations;

public enum ReleaseInventoryOutcome
{
    Released = 0,
    AlreadyReleased = 1,
    AlreadyExpired = 2,
    NotFound = 3
}
