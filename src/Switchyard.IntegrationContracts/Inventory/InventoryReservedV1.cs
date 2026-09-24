namespace Switchyard.IntegrationContracts.Inventory;

public sealed record InventoryReservedV1(
    Guid RequestId,
    Guid OrderId,
    Guid OrderLineId,
    string SkuCode,
    int Quantity,
    Guid ReservationId,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public const string MessageType = "inventory.event.reserved.v1";
}
