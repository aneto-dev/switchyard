namespace Switchyard.Api.Ordering;

public sealed record CreateOrderResponse(
    Guid OrderId, string OrderNumber, string Status, decimal TotalAmount,
    string Currency, DateTimeOffset CreatedAtUtc);

public sealed record CreateOrderConflictResponse(string Message);

public sealed record OrderResponse(
    Guid OrderId, string OrderNumber, string Status, decimal TotalAmount,
    string Currency, DateTimeOffset CreatedAtUtc, IReadOnlyList<OrderLineResponse> Lines);

public sealed record OrderLineResponse(
    Guid OrderLineId, string SkuCode, string ProductName, int Quantity,
    decimal UnitPriceAmount, decimal LineTotalAmount, string Currency);
