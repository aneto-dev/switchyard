namespace Switchyard.IntegrationContracts.Inventory;

public sealed record ReserveInventoryV1(
    Guid RequestId,
    Guid OrderId,
    Guid OrderLineId,
    string SkuCode,
    int Quantity)
{
    public const string MessageType = "inventory.command.reserve.v1";
}
