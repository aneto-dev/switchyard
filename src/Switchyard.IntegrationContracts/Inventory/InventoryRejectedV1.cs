namespace Switchyard.IntegrationContracts.Inventory;

public sealed record InventoryRejectedV1(
    Guid RequestId,
    Guid OrderId,
    Guid OrderLineId,
    string SkuCode,
    int Quantity,
    string Reason,
    DateTimeOffset RejectedAtUtc)
{
    public const string MessageType = "inventory.event.rejected.v1";

    public const string InsufficientStockReason = "InsufficientStock";
}
