using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Operations;

// PlacementState is null for orders accepted before the placement process existed.
public sealed record OperationsOrderSummary(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    OrderPlacementState? PlacementState,
    decimal TotalAmount,
    string Currency,
    int LineCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PlacementUpdatedAtUtc);
