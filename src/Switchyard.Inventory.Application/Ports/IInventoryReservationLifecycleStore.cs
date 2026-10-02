using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Application.Ports;

public interface IInventoryReservationLifecycleStore
{
    Task<InventoryReservationReleaseDecision> ReleaseAsync(
        Guid requestId,
        Guid orderId,
        Guid reservationId,
        StockReservationReleaseReason reason,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken);

    Task<int> ExpireAsync(
        DateTimeOffset expiredAtUtc,
        int batchSize,
        CancellationToken cancellationToken);
}
