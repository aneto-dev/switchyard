using Switchyard.Inventory.Domain.Stock;

namespace Switchyard.Inventory.Application.Reservations;

public sealed record InventoryReservationRequest(
    Guid RequestId,
    Guid OrderId,
    InventorySku Sku,
    int Quantity,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc);
