namespace Switchyard.Ordering.Application.Orders;

public sealed record CreatePendingOrderLine(
    string SkuCode,
    string ProductName,
    int Quantity,
    decimal UnitPriceAmount,
    string Currency);
