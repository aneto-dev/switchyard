namespace Switchyard.Ordering.Application.Orders;

public sealed record GetOrderResult(
    Guid OrderId,
    string OrderNumber,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<GetOrderLineResult> Lines);
