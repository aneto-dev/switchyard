using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Infrastructure.Persistence;

internal sealed class ReservationRequestRecord
{
    public Guid RequestId { get; set; }
    public Guid OrderId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public InventoryReservationOutcome Outcome { get; set; }
    public Guid? ReservationId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public StockReservationReleaseReason? ReleaseReason { get; set; }
    public DateTimeOffset? ExpiredAtUtc { get; set; }
}
