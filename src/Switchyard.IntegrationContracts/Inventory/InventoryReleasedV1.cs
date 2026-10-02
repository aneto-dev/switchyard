namespace Switchyard.IntegrationContracts.Inventory;

public sealed record InventoryReleasedV1(
    Guid RequestId,
    Guid OrderId,
    Guid ReservationId,
    string Reason,
    DateTimeOffset ReleasedAtUtc)
{
    public const string MessageType = "inventory.event.released.v1";

    public const string CompensationReason = "Compensation";
    public const string CancellationReason = "Cancellation";
}
