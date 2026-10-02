using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Application.Reservations;

public sealed record InventoryReservationReleaseDecision(
    Guid RequestId,
    Guid OrderId,
    Guid ReservationId,
    ReleaseInventoryOutcome Outcome,
    StockReservationReleaseReason? ReleaseReason,
    DateTimeOffset? ReleasedAtUtc,
    DateTimeOffset? ExpiredAtUtc);
