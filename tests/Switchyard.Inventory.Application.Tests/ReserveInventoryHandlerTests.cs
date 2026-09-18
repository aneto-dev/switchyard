using Switchyard.Inventory.Application.Ports;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Xunit;

namespace Switchyard.Inventory.Application.Tests;

public sealed class ReserveInventoryHandlerTests
{
    [Fact]
    public async Task AppliesConfiguredReservationLifetime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 18, 9, 30, 0, TimeSpan.Zero);
        var store = new RecordingReservationStore();
        var handler = new ReserveInventoryHandler(
            store,
            new InventoryReservationPolicy(TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(now));
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(requestId, orderId, "BIKE-001", 2),
            cancellationToken);

        Assert.NotNull(store.Request);
        Assert.Equal(now, store.Request!.RequestedAtUtc);
        Assert.Equal(now.AddMinutes(15), store.Request.ExpiresAtUtc);
        Assert.Equal(InventoryReservationOutcome.Reserved, result.Outcome);
        Assert.False(result.Replayed);
        Assert.NotNull(result.ReservationId);
        Assert.Equal(now.AddMinutes(15), result.ExpiresAtUtc);
    }

    [Fact]
    public async Task RejectsInvalidQuantityBeforeCallingStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new RecordingReservationStore();
        var handler = new ReserveInventoryHandler(
            store,
            new InventoryReservationPolicy(TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(
                new ReserveInventoryCommand(Guid.NewGuid(), Guid.NewGuid(), "BIKE-001", 0),
                cancellationToken));

        Assert.Null(store.Request);
    }

    private sealed class RecordingReservationStore : IInventoryReservationStore
    {
        public InventoryReservationRequest? Request { get; private set; }

        public Task<InventoryReservationDecision> ReserveAsync(
            InventoryReservationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;

            var reservation = new StockReservation(
                StockReservationId.New(),
                request.RequestId,
                request.OrderId,
                request.Sku,
                request.Quantity,
                request.RequestedAtUtc,
                request.ExpiresAtUtc);

            return Task.FromResult(
                new InventoryReservationDecision(
                    InventoryReservationOutcome.Reserved,
                    reservation,
                    Replayed: false));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
