using Switchyard.Inventory.Application.Ports;

namespace Switchyard.Inventory.Application.Reservations;

public sealed class ExpireInventoryReservationsHandler
{
    private readonly IInventoryReservationLifecycleStore _lifecycleStore;
    private readonly TimeProvider _timeProvider;

    public ExpireInventoryReservationsHandler(
        IInventoryReservationLifecycleStore lifecycleStore, TimeProvider timeProvider)
    {
        _lifecycleStore = lifecycleStore ?? throw new ArgumentNullException(nameof(lifecycleStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ExpireInventoryReservationsResult> HandleAsync(
        ExpireInventoryReservationsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command), command.BatchSize,
                "Expiry batch size must be positive.");
        }

        var expiredAtUtc = _timeProvider.GetUtcNow();
        var expiredCount = await _lifecycleStore.ExpireAsync(
            expiredAtUtc, command.BatchSize,
            cancellationToken);

        return new ExpireInventoryReservationsResult(expiredCount, expiredAtUtc);
    }
}
