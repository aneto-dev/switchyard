namespace Switchyard.IntegrationContracts.Inventory;

public sealed record ReleaseInventoryReservationV1(
    Guid OrderId,
    Guid OrderLineId,
    Guid ReservationId,
    string Reason)
{
    public const string MessageType = "inventory.command.release.v1";

    public const string CompensationReason = "Compensation";
}
