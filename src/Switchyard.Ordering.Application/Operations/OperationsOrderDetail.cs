using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Operations;

public sealed record OperationsOrderDetail(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    OperationsOrderPlacement? Placement,
    IReadOnlyList<OperationsOrderLine> Lines);

public sealed record OperationsOrderPlacement(
    OrderPlacementState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? PaymentAuthorisationRequestId);

// Reservation fields are null when the line has no placement record or Inventory has not
// reserved stock for it yet.
public sealed record OperationsOrderLine(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount,
    string Currency,
    OrderPlacementLineState? ReservationState,
    Guid? ReservationRequestId,
    Guid? ReservationId,
    DateTimeOffset? ReservationExpiresAtUtc);
