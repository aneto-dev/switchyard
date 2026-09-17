namespace Switchyard.Ordering.Application.Orders;

public sealed record CreatePendingOrderResult(
    Guid OrderId,
    string OrderNumber,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAtUtc);
