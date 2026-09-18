using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Application.Reservations;

public sealed record InventoryReservationDecision(
    InventoryReservationOutcome Outcome,
    StockReservation? Reservation,
    bool Replayed);
