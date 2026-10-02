using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Application.Reservations;

public sealed record ReleaseInventoryCommand(
    Guid RequestId,
    Guid OrderId,
    Guid ReservationId,
    StockReservationReleaseReason Reason);
