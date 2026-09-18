using Switchyard.Inventory.Application.Ports;
using Switchyard.Inventory.Domain.Stock;

namespace Switchyard.Inventory.Application.Reservations;

public sealed class ReserveInventoryHandler
{
    private readonly IInventoryReservationStore _reservationStore;
    private readonly InventoryReservationPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public ReserveInventoryHandler(
        IInventoryReservationStore reservationStore, InventoryReservationPolicy policy,
        TimeProvider timeProvider)
    {
        _reservationStore = reservationStore ?? throw new ArgumentNullException(nameof(reservationStore));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReserveInventoryResult> HandleAsync(
        ReserveInventoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestId == Guid.Empty)
        {
            throw new ArgumentException("Reservation request ID cannot be empty.", nameof(command));
        }

        if (command.OrderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(command));
        }

        if (command.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.Quantity,
                "Reservation quantity must be positive.");
        }

        var sku = new InventorySku(command.SkuCode);
        var requestedAtUtc = _timeProvider.GetUtcNow();
        var request = new InventoryReservationRequest(
            command.RequestId,
            command.OrderId,
            sku,
            command.Quantity,
            requestedAtUtc,
            _policy.CalculateExpiry(requestedAtUtc));

        var decision = await _reservationStore.ReserveAsync(request, cancellationToken);

        return decision.Outcome switch
        {
            InventoryReservationOutcome.Reserved when decision.Reservation is not null =>
                new ReserveInventoryResult(
                    command.RequestId,
                    command.OrderId,
                    sku.Value,
                    command.Quantity,
                    decision.Outcome,
                    decision.Reservation.Id.Value,
                    decision.Reservation.ReservedAtUtc,
                    decision.Reservation.ExpiresAtUtc,
                    decision.Replayed),

            InventoryReservationOutcome.InsufficientStock when decision.Reservation is null =>
                new ReserveInventoryResult(
                    command.RequestId,
                    command.OrderId,
                    sku.Value,
                    command.Quantity,
                    decision.Outcome,
                    null,
                    null,
                    null,
                    decision.Replayed),

            _ => throw new InvalidOperationException("Inventory reservation store returned an invalid decision.")
        };
    }
}
