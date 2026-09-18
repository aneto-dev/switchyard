using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class InventoryReservationLifecycleTests
{
    [Fact]
    public async Task ReleaseReturnsStockExactlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);
        await SeedStockAsync(postgres.GetConnectionString(), "BIKE-RELEASE", 1, cancellationToken);

        var reservedAt = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var reservationId = await ReserveAsync(postgres.GetConnectionString(), "BIKE-RELEASE", reservedAt, cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var store = new EfInventoryReservationLifecycleStore(dbContext);

        var first = await store.ReleaseAsync(
            reservationId, StockReservationReleaseReason.Compensation,
            reservedAt.AddMinutes(2), cancellationToken);
        var second = await store.ReleaseAsync(
            reservationId, StockReservationReleaseReason.Compensation,
            reservedAt.AddMinutes(3), cancellationToken);

        Assert.Equal(ReleaseInventoryOutcome.Released, first);
        Assert.Equal(ReleaseInventoryOutcome.AlreadyReleased, second);
        Assert.Equal(0, await ReadReservedQuantityAsync(postgres.GetConnectionString(), "BIKE-RELEASE", cancellationToken));

        var lifecycle = await ReadLifecycleAsync(postgres.GetConnectionString(), reservationId, cancellationToken);
        Assert.Equal(reservedAt.AddMinutes(2), lifecycle.ReleasedAtUtc);
        Assert.Equal((int)StockReservationReleaseReason.Compensation, lifecycle.ReleaseReason);
        Assert.Null(lifecycle.ExpiredAtUtc);
    }

    [Fact]
    public async Task ExpiryReturnsExpiredStockAndLeavesFutureReservationActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);
        await SeedStockAsync(postgres.GetConnectionString(), "BIKE-EXPIRED", 1, cancellationToken);
        await SeedStockAsync(postgres.GetConnectionString(), "BIKE-FUTURE", 1, cancellationToken);

        var start = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var expiredId = await ReserveAsync(postgres.GetConnectionString(), "BIKE-EXPIRED", start, cancellationToken);
        var futureId = await ReserveAsync(postgres.GetConnectionString(), "BIKE-FUTURE", start.AddMinutes(10), cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var store = new EfInventoryReservationLifecycleStore(dbContext);
        var expiredCount = await store.ExpireAsync(start.AddMinutes(16), 10, cancellationToken);

        Assert.Equal(1, expiredCount);
        Assert.Equal(0, await ReadReservedQuantityAsync(postgres.GetConnectionString(), "BIKE-EXPIRED", cancellationToken));
        Assert.Equal(1, await ReadReservedQuantityAsync(postgres.GetConnectionString(), "BIKE-FUTURE", cancellationToken));

        var expired = await ReadLifecycleAsync(postgres.GetConnectionString(), expiredId, cancellationToken);
        var future = await ReadLifecycleAsync(postgres.GetConnectionString(), futureId, cancellationToken);
        Assert.Equal(start.AddMinutes(16), expired.ExpiredAtUtc);
        Assert.Null(expired.ReleasedAtUtc);
        Assert.Null(future.ExpiredAtUtc);
        Assert.Null(future.ReleasedAtUtc);
    }

    [Fact]
    public async Task ConcurrentReleaseAndExpiryReturnStockOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);
        await SeedStockAsync(postgres.GetConnectionString(), "BIKE-RACE", 1, cancellationToken);

        var reservedAt = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var transitionAt = reservedAt.AddMinutes(16);
        var reservationId = await ReserveAsync(postgres.GetConnectionString(), "BIKE-RACE", reservedAt, cancellationToken);

        await using var releaseContext = CreateDbContext(postgres.GetConnectionString());
        await using var expiryContext = CreateDbContext(postgres.GetConnectionString());
        var releaseStore = new EfInventoryReservationLifecycleStore(releaseContext);
        var expiryStore = new EfInventoryReservationLifecycleStore(expiryContext);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var releaseTask = Task.Run(async () =>
        {
            ready.Signal();
            start.Wait(cancellationToken);
            return await releaseStore.ReleaseAsync(
                reservationId, StockReservationReleaseReason.Cancellation,
                transitionAt, cancellationToken);
        }, cancellationToken);

        var expiryTask = Task.Run(async () =>
        {
            ready.Signal();
            start.Wait(cancellationToken);
            return await expiryStore.ExpireAsync(transitionAt, 10, cancellationToken);
        }, cancellationToken);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        start.Set();

        var releaseOutcome = await releaseTask;
        var expiredCount = await expiryTask;

        Assert.True(
            (releaseOutcome == ReleaseInventoryOutcome.Released && expiredCount == 0) ||
            (releaseOutcome == ReleaseInventoryOutcome.AlreadyExpired && expiredCount == 1));
        Assert.Equal(0, await ReadReservedQuantityAsync(postgres.GetConnectionString(), "BIKE-RACE", cancellationToken));

        var lifecycle = await ReadLifecycleAsync(postgres.GetConnectionString(), reservationId, cancellationToken);
        Assert.NotEqual(lifecycle.ReleasedAtUtc is null, lifecycle.ExpiredAtUtc is null);
    }

    private static async Task<Guid> ReserveAsync(
        string connectionString, string skuCode, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        var handler = new ReserveInventoryHandler(
            new EfInventoryReservationStore(dbContext),
            new InventoryReservationPolicy(TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(now));
        var result = await handler.HandleAsync(
            new ReserveInventoryCommand(Guid.NewGuid(), Guid.NewGuid(), skuCode, 1),
            cancellationToken);
        Assert.Equal(InventoryReservationOutcome.Reserved, result.Outcome);
        Assert.NotNull(result.ReservationId);
        return result.ReservationId.Value;
    }

    private static InventoryDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(connectionString).Options;
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
        string connectionString, string skuCode, int onHandQuantity,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO inventory.stock_items (sku_code, on_hand_quantity, reserved_quantity) VALUES (@sku, @qty, 0);",
            connection);
        command.Parameters.AddWithValue("sku", skuCode);
        command.Parameters.AddWithValue("qty", onHandQuantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReadReservedQuantityAsync(
        string connectionString, string skuCode, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT reserved_quantity FROM inventory.stock_items WHERE sku_code = @sku;",
            connection);
        command.Parameters.AddWithValue("sku", skuCode);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<LifecycleRow> ReadLifecycleAsync(
        string connectionString, Guid reservationId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT released_at_utc, release_reason, expired_at_utc FROM inventory.reservation_requests WHERE reservation_id = @id;",
            connection);
        command.Parameters.AddWithValue("id", reservationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return new LifecycleRow(
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private sealed record LifecycleRow(DateTimeOffset? ReleasedAtUtc, int? ReleaseReason, DateTimeOffset? ExpiredAtUtc);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) { _utcNow = utcNow; }
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
