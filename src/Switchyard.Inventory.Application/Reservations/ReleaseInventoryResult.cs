using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Application.Reservations;

public sealed record ReleaseInventoryResult(
    Guid ReservationId,
    StockReservationReleaseReason Reason,
    ReleaseInventoryOutcome Outcome,
    DateTimeOffset AttemptedAtUtc);
