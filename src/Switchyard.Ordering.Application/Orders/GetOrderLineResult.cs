namespace Switchyard.Ordering.Application.Orders;

public sealed record GetOrderLineResult(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount,
    string Currency);
