namespace Switchyard.Inventory.Application.Reservations;

public sealed record ReserveInventoryCommand(
    Guid RequestId,
    Guid OrderId,
    string SkuCode,
    int Quantity);
