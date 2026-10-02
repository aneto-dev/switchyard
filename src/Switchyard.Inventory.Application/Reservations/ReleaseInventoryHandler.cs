using Switchyard.Inventory.Application.Ports;

namespace Switchyard.Inventory.Application.Reservations;

public sealed class ReleaseInventoryHandler
{
    private readonly IInventoryReservationLifecycleStore _lifecycleStore;
    private readonly TimeProvider _timeProvider;

    public ReleaseInventoryHandler(
        IInventoryReservationLifecycleStore lifecycleStore,
        TimeProvider timeProvider)
    {
        _lifecycleStore =
            lifecycleStore ??
            throw new ArgumentNullException(nameof(lifecycleStore));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReleaseInventoryResult> HandleAsync(
        ReleaseInventoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation request ID cannot be empty.",
                nameof(command));
        }

        if (command.OrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order ID cannot be empty.",
                nameof(command));
        }

        if (command.ReservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation ID cannot be empty.",
                nameof(command));
        }

        if (!Enum.IsDefined(command.Reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.Reason,
                "Reservation release reason is not supported.");
        }

        var attemptedAtUtc =
            _timeProvider.GetUtcNow();

        var decision =
            await _lifecycleStore.ReleaseAsync(
                command.RequestId,
                command.OrderId,
                command.ReservationId,
                command.Reason,
                attemptedAtUtc,
                cancellationToken);

        if (decision.RequestId != command.RequestId ||
            decision.OrderId != command.OrderId ||
            decision.ReservationId != command.ReservationId)
        {
            throw new InvalidOperationException(
                "Inventory reservation lifecycle store returned a mismatched release decision.");
        }

        return new ReleaseInventoryResult(
            decision.RequestId,
            decision.OrderId,
            decision.ReservationId,
            command.Reason,
            decision.Outcome,
            decision.ReleaseReason,
            decision.ReleasedAtUtc,
            decision.ExpiredAtUtc,
            attemptedAtUtc);
    }
}
