using Switchyard.Inventory.Application.Reservations;

namespace Switchyard.Inventory.Application.Ports;

public interface IInventoryReservationStore
{
    Task<InventoryReservationDecision> ReserveAsync(
        InventoryReservationRequest request, CancellationToken cancellationToken);
}
