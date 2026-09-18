namespace Switchyard.Inventory.Application.Reservations;

public sealed record ReserveInventoryResult(
    Guid RequestId,
    Guid OrderId,
    string SkuCode,
    int Quantity,
    InventoryReservationOutcome Outcome,
    Guid? ReservationId,
    DateTimeOffset? ReservedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool Replayed);
