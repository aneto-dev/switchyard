using Switchyard.Inventory.Application.Ports;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Xunit;

namespace Switchyard.Inventory.Application.Tests;

public sealed class InventoryReservationLifecycleHandlerTests
{
    [Fact]
    public async Task ReleaseUsesCurrentTimeIdentityAndReason()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var now =
            new DateTimeOffset(
                2026,
                9,
                18,
                15,
                30,
                0,
                TimeSpan.Zero);

        var requestId =
            Guid.NewGuid();

        var orderId =
            Guid.NewGuid();

        var reservationId =
            Guid.NewGuid();

        var store =
            new RecordingLifecycleStore
            {
                ReleaseOutcome =
                    ReleaseInventoryOutcome.Released
            };

        var handler =
            new ReleaseInventoryHandler(
                store,
                new FixedTimeProvider(now));

        var result =
            await handler.HandleAsync(
                new ReleaseInventoryCommand(
                    requestId,
                    orderId,
                    reservationId,
                    StockReservationReleaseReason.Compensation),
                cancellationToken);

        Assert.Equal(
            requestId,
            store.RequestId);

        Assert.Equal(
            orderId,
            store.OrderId);

        Assert.Equal(
            reservationId,
            store.ReservationId);

        Assert.Equal(
            StockReservationReleaseReason.Compensation,
            store.ReleaseReason);

        Assert.Equal(
            now,
            store.TransitionedAtUtc);

        Assert.Equal(
            ReleaseInventoryOutcome.Released,
            result.Outcome);

        Assert.Equal(
            now,
            result.ReleasedAtUtc);
    }

    [Fact]
    public async Task ReleaseRejectsUnsupportedReasonBeforeCallingStore()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var store =
            new RecordingLifecycleStore();

        var handler =
            new ReleaseInventoryHandler(
                store,
                new FixedTimeProvider(
                    DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
                handler.HandleAsync(
                    new ReleaseInventoryCommand(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        (StockReservationReleaseReason)999),
                    cancellationToken));

        Assert.Null(
            store.ReservationId);

        Assert.Null(
            store.ReleaseReason);

        Assert.Null(
            store.TransitionedAtUtc);
    }

    [Fact]
    public async Task ExpiryUsesCurrentTimeAndBatchSize()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var now =
            new DateTimeOffset(
                2026,
                9,
                18,
                16,
                0,
                0,
                TimeSpan.Zero);

        var store =
            new RecordingLifecycleStore
            {
                ExpiredCount = 7
            };

        var handler =
            new ExpireInventoryReservationsHandler(
                store,
                new FixedTimeProvider(now));

        var result =
            await handler.HandleAsync(
                new ExpireInventoryReservationsCommand(25),
                cancellationToken);

        Assert.Equal(
            now,
            store.TransitionedAtUtc);

        Assert.Equal(
            25,
            store.BatchSize);

        Assert.Equal(
            7,
            result.ExpiredCount);
    }

    [Fact]
    public async Task ExpiryRejectsInvalidBatchSizeBeforeCallingStore()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var store =
            new RecordingLifecycleStore();

        var handler =
            new ExpireInventoryReservationsHandler(
                store,
                new FixedTimeProvider(
                    DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
                handler.HandleAsync(
                    new ExpireInventoryReservationsCommand(0),
                    cancellationToken));

        Assert.Null(
            store.TransitionedAtUtc);
    }

    private sealed class RecordingLifecycleStore :
        IInventoryReservationLifecycleStore
    {
        public ReleaseInventoryOutcome ReleaseOutcome { get; init; }
        public int ExpiredCount { get; init; }
        public Guid? RequestId { get; private set; }
        public Guid? OrderId { get; private set; }
        public Guid? ReservationId { get; private set; }
        public StockReservationReleaseReason? ReleaseReason { get; private set; }
        public DateTimeOffset? TransitionedAtUtc { get; private set; }
        public int? BatchSize { get; private set; }

        public Task<InventoryReservationReleaseDecision> ReleaseAsync(
            Guid requestId,
            Guid orderId,
            Guid reservationId,
            StockReservationReleaseReason reason,
            DateTimeOffset releasedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RequestId =
                requestId;

            OrderId =
                orderId;

            ReservationId =
                reservationId;

            ReleaseReason =
                reason;

            TransitionedAtUtc =
                releasedAtUtc;

            return Task.FromResult(
                new InventoryReservationReleaseDecision(
                    requestId,
                    orderId,
                    reservationId,
                    ReleaseOutcome,
                    reason,
                    ReleaseOutcome is
                        ReleaseInventoryOutcome.Released or
                        ReleaseInventoryOutcome.AlreadyReleased
                            ? releasedAtUtc
                            : null,
                    ReleaseOutcome ==
                        ReleaseInventoryOutcome.AlreadyExpired
                            ? releasedAtUtc
                            : null));
        }

        public Task<int> ExpireAsync(
            DateTimeOffset expiredAtUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TransitionedAtUtc =
                expiredAtUtc;

            BatchSize =
                batchSize;

            return Task.FromResult(
                ExpiredCount);
        }
    }

    private sealed class FixedTimeProvider :
        TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;
    }
}
