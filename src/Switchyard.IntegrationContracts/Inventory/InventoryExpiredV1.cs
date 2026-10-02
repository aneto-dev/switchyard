namespace Switchyard.IntegrationContracts.Inventory;

public sealed record InventoryExpiredV1(
    Guid RequestId,
    Guid OrderId,
    Guid ReservationId,
    DateTimeOffset ExpiredAtUtc)
{
    public const string MessageType = "inventory.event.expired.v1";
}
