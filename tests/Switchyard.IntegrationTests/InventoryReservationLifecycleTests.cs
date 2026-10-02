using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class InventoryReservationLifecycleTests
{
    [Fact]
    public async Task ReleaseReturnsStockExactlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-RELEASE", 1, cancellationToken);

        var reservedAt = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var reservation = await ReserveAsync(connectionString, "BIKE-RELEASE", reservedAt, cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var store = new EfInventoryReservationLifecycleStore(dbContext);

        var first = await store.ReleaseAsync(
            reservation.RequestId,
            reservation.OrderId,
            reservation.ReservationId,
            StockReservationReleaseReason.Compensation,
            reservedAt.AddMinutes(2),
            cancellationToken);

        var second = await store.ReleaseAsync(
            reservation.RequestId,
            reservation.OrderId,
            reservation.ReservationId,
            StockReservationReleaseReason.Compensation,
            reservedAt.AddMinutes(3),
            cancellationToken);

        Assert.Equal(ReleaseInventoryOutcome.Released, first.Outcome);
        Assert.Equal(ReleaseInventoryOutcome.AlreadyReleased, second.Outcome);
        Assert.Equal(reservedAt.AddMinutes(2), second.ReleasedAtUtc);
        Assert.Equal(0, await ReadReservedQuantityAsync(connectionString, "BIKE-RELEASE", cancellationToken));

        var lifecycle = await ReadLifecycleAsync(connectionString, reservation.ReservationId, cancellationToken);
        Assert.Equal(reservedAt.AddMinutes(2), lifecycle.ReleasedAtUtc);
        Assert.Equal((int)StockReservationReleaseReason.Compensation, lifecycle.ReleaseReason);
        Assert.Null(lifecycle.ExpiredAtUtc);
    }

    [Fact]
    public async Task ExpiryReturnsExpiredStockAndLeavesFutureReservationActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-EXPIRED", 1, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-FUTURE", 1, cancellationToken);

        var start = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var expired = await ReserveAsync(connectionString, "BIKE-EXPIRED", start, cancellationToken);
        var future = await ReserveAsync(connectionString, "BIKE-FUTURE", start.AddMinutes(10), cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var store = new EfInventoryReservationLifecycleStore(dbContext);
        var expiredCount = await store.ExpireAsync(start.AddMinutes(16), 10, cancellationToken);

        Assert.Equal(1, expiredCount);
        Assert.Equal(0, await ReadReservedQuantityAsync(connectionString, "BIKE-EXPIRED", cancellationToken));
        Assert.Equal(1, await ReadReservedQuantityAsync(connectionString, "BIKE-FUTURE", cancellationToken));

        var expiredLifecycle = await ReadLifecycleAsync(connectionString, expired.ReservationId, cancellationToken);
        var futureLifecycle = await ReadLifecycleAsync(connectionString, future.ReservationId, cancellationToken);
        Assert.Equal(start.AddMinutes(16), expiredLifecycle.ExpiredAtUtc);
        Assert.Null(expiredLifecycle.ReleasedAtUtc);
        Assert.Null(futureLifecycle.ExpiredAtUtc);
        Assert.Null(futureLifecycle.ReleasedAtUtc);
    }

    [Fact]
    public async Task ConcurrentReleaseAndExpiryReturnStockOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-RACE", 1, cancellationToken);

        var reservedAt = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var transitionAt = reservedAt.AddMinutes(16);
        var reservation = await ReserveAsync(connectionString, "BIKE-RACE", reservedAt, cancellationToken);

        await using var releaseContext = CreateDbContext(connectionString);
        await using var expiryContext = CreateDbContext(connectionString);
        var releaseStore = new EfInventoryReservationLifecycleStore(releaseContext);
        var expiryStore = new EfInventoryReservationLifecycleStore(expiryContext);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var releaseTask = Task.Run(async () =>
        {
            ready.Signal();
            start.Wait(cancellationToken);
            return await releaseStore.ReleaseAsync(
                reservation.RequestId,
                reservation.OrderId,
                reservation.ReservationId,
                StockReservationReleaseReason.Cancellation,
                transitionAt,
                cancellationToken);
        }, cancellationToken);

        var expiryTask = Task.Run(async () =>
        {
            ready.Signal();
            start.Wait(cancellationToken);
            return await expiryStore.ExpireAsync(transitionAt, 10, cancellationToken);
        }, cancellationToken);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        start.Set();

        var releaseDecision = await releaseTask;
        var expiredCount = await expiryTask;

        Assert.True(
            (releaseDecision.Outcome == ReleaseInventoryOutcome.Released && expiredCount == 0) ||
            (releaseDecision.Outcome == ReleaseInventoryOutcome.AlreadyExpired && expiredCount == 1));
        Assert.Equal(0, await ReadReservedQuantityAsync(connectionString, "BIKE-RACE", cancellationToken));

        var lifecycle = await ReadLifecycleAsync(connectionString, reservation.ReservationId, cancellationToken);
        Assert.NotEqual(lifecycle.ReleasedAtUtc is null, lifecycle.ExpiredAtUtc is null);
    }

    [Fact]
    public async Task ReleaseInsideInboxRollsBackWhenLaterWorkFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-ROLLBACK", 1, cancellationToken);

        var now = new DateTimeOffset(2026, 9, 27, 17, 30, 0, TimeSpan.Zero);
        var reservation = await ReserveAsync(connectionString, "BIKE-ROLLBACK", now, cancellationToken);
        var incoming = new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            "inventory.command.release.rollback-test.v1",
            "{}",
            now.AddMinutes(1),
            reservation.OrderId,
            Guid.NewGuid());

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var processor = new EfInventoryInboxMessageProcessor(
                dbContext,
                new FixedTimeProvider(now.AddMinutes(1)));
            var store = new EfInventoryReservationLifecycleStore(dbContext);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => processor.ProcessAsync(
                    "inventory-release-rollback-test",
                    incoming,
                    async token =>
                    {
                        var decision = await store.ReleaseAsync(
                            reservation.RequestId,
                            reservation.OrderId,
                            reservation.ReservationId,
                            StockReservationReleaseReason.Compensation,
                            now.AddMinutes(1),
                            token);

                        Assert.Equal(ReleaseInventoryOutcome.Released, decision.Outcome);
                        throw new InvalidOperationException("Simulated failure after local release.");
                    },
                    cancellationToken));
        }

        Assert.Equal(1, await ReadReservedQuantityAsync(connectionString, "BIKE-ROLLBACK", cancellationToken));
        var lifecycle = await ReadLifecycleAsync(connectionString, reservation.ReservationId, cancellationToken);
        Assert.Null(lifecycle.ReleasedAtUtc);
        Assert.Null(lifecycle.ExpiredAtUtc);
        Assert.Equal(0, await CountRowsAsync(connectionString, "inventory.inbox_messages", cancellationToken));
    }

    private static async Task<ReservationIdentity> ReserveAsync(
        string connectionString,
        string skuCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(connectionString);
        var handler = new ReserveInventoryHandler(
            new EfInventoryReservationStore(dbContext),
            new InventoryReservationPolicy(TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(requestId, orderId, skuCode, 1),
            cancellationToken);

        Assert.Equal(InventoryReservationOutcome.Reserved, result.Outcome);
        Assert.NotNull(result.ReservationId);
        return new ReservationIdentity(requestId, orderId, result.ReservationId.Value);
    }

    private static InventoryDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new InventoryDbContext(options);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_inventory_lifecycle_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static async Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task SeedStockAsync(
        string connectionString,
        string skuCode,
        int onHandQuantity,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO inventory.stock_items
                (sku_code, on_hand_quantity, reserved_quantity)
            VALUES
                (@sku, @qty, 0);
            """,
            connection);
        command.Parameters.AddWithValue("sku", skuCode);
        command.Parameters.AddWithValue("qty", onHandQuantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReadReservedQuantityAsync(
        string connectionString,
        string skuCode,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT reserved_quantity
            FROM inventory.stock_items
            WHERE sku_code = @sku;
            """,
            connection);
        command.Parameters.AddWithValue("sku", skuCode);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<LifecycleRow> ReadLifecycleAsync(
        string connectionString,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT released_at_utc, release_reason, expired_at_utc
            FROM inventory.reservation_requests
            WHERE reservation_id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", reservationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return new LifecycleRow(
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private static async Task<int> CountRowsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {tableName};", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private sealed record ReservationIdentity(Guid RequestId, Guid OrderId, Guid ReservationId);
    private sealed record LifecycleRow(DateTimeOffset? ReleasedAtUtc, int? ReleaseReason, DateTimeOffset? ExpiredAtUtc);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) { _utcNow = utcNow; }
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
