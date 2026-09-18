using Switchyard.Inventory.Application.Ports;

namespace Switchyard.Inventory.Application.Reservations;

public sealed class ReleaseInventoryHandler
{
    private readonly IInventoryReservationLifecycleStore _lifecycleStore;
    private readonly TimeProvider _timeProvider;

    public ReleaseInventoryHandler(IInventoryReservationLifecycleStore lifecycleStore, TimeProvider timeProvider)
    {
        _lifecycleStore = lifecycleStore ?? throw new ArgumentNullException(nameof(lifecycleStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReleaseInventoryResult> HandleAsync(
        ReleaseInventoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ReservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation ID cannot be empty.", nameof(command));
        }

        var attemptedAtUtc = _timeProvider.GetUtcNow();
        var outcome = await _lifecycleStore.ReleaseAsync(
            command.ReservationId, command.Reason,
            attemptedAtUtc, cancellationToken);

        return new ReleaseInventoryResult(
            command.ReservationId,
            command.Reason,
            outcome,
            attemptedAtUtc);
    }
}
