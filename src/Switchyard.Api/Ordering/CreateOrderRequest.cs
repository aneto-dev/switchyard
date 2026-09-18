namespace Switchyard.Api.Ordering;

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderLineRequest?>? Lines);

public sealed record CreateOrderLineRequest(
    string? SkuCode, string? ProductName, int Quantity, decimal UnitPriceAmount, string? Currency);
