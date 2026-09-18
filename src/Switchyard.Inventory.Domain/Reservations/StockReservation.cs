using Switchyard.Inventory.Domain.Stock;

namespace Switchyard.Inventory.Domain.Reservations;

public sealed record StockReservation
{
    public StockReservation(
        StockReservationId id, Guid requestId, Guid orderId, InventorySku sku,
        int quantity, DateTimeOffset reservedAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sku);

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Reservation request ID cannot be empty.", nameof(requestId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(orderId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Reservation quantity must be positive.");
        }

        if (expiresAtUtc <= reservedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "Reservation expiry must be later than the reservation time.");
        }

        Id = id;
        RequestId = requestId;
        OrderId = orderId;
        Sku = sku;
        Quantity = quantity;
        ReservedAtUtc = reservedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public StockReservationId Id { get; }

    public Guid RequestId { get; }

    public Guid OrderId { get; }

    public InventorySku Sku { get; }

    public int Quantity { get; }

    public DateTimeOffset ReservedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }
}
