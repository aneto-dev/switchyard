using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Application.Reservations;

public sealed record ReleaseInventoryCommand(Guid ReservationId, StockReservationReleaseReason Reason);
