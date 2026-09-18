using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class InventoryReservationTests
{
    [Fact]
    public async Task ReservesStockAndReplaysSameRequestIdempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);
        await SeedStockAsync(postgres.GetConnectionString(), "BIKE-001", 2, cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 9, 45, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateHandler(dbContext, now);

        var first = await handler.HandleAsync(
            new ReserveInventoryCommand(requestId, orderId, "BIKE-001", 1),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new ReserveInventoryCommand(requestId, orderId, "BIKE-001", 1),
            cancellationToken);

        Assert.Equal(InventoryReservationOutcome.Reserved, first.Outcome);
        Assert.False(first.Replayed);
        Assert.NotNull(first.ReservationId);
        Assert.Equal(now.AddMinutes(15), first.ExpiresAtUtc);

        Assert.Equal(first.ReservationId, replay.ReservationId);
        Assert.True(replay.Replayed);

        Assert.Equal(
            1,
            await ReadReservedQuantityAsync(
                postgres.GetConnectionString(),
                "BIKE-001",
                cancellationToken));
        Assert.Equal(
            1,
            await CountReservationRequestsAsync(
                postgres.GetConnectionString(),
                cancellationToken));

        await Assert.ThrowsAsync<InventoryReservationConflictException>(
            () => handler.HandleAsync(
                new ReserveInventoryCommand(requestId, orderId, "BIKE-001", 2),
                cancellationToken));
    }

    [Fact]
    public async Task ConcurrentRequestsForLastUnitProduceOneReservationAndOneRejection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);
        await SeedStockAsync(postgres.GetConnectionString(), "HELMET-001", 1, cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

        await using var firstContext = CreateDbContext(postgres.GetConnectionString());
        await using var secondContext = CreateDbContext(postgres.GetConnectionString());

        var firstHandler = CreateHandler(firstContext, now);
        var secondHandler = CreateHandler(secondContext, now);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var firstTask = Task.Run(
            async () =>
            {
                ready.Signal();
                start.Wait(cancellationToken);

                return await firstHandler.HandleAsync(
                    new ReserveInventoryCommand(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "HELMET-001",
                        1),
                    cancellationToken);
            },
            cancellationToken);

        var secondTask = Task.Run(
            async () =>
            {
                ready.Signal();
                start.Wait(cancellationToken);

                return await secondHandler.HandleAsync(
                    new ReserveInventoryCommand(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "HELMET-001",
                        1),
                    cancellationToken);
            },
            cancellationToken);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        start.Set();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(
            results,
            result => result.Outcome == InventoryReservationOutcome.Reserved);
        Assert.Single(
            results,
            result => result.Outcome == InventoryReservationOutcome.InsufficientStock);

        Assert.Equal(
            1,
            await ReadReservedQuantityAsync(
                postgres.GetConnectionString(),
                "HELMET-001",
                cancellationToken));
        Assert.Equal(
            2,
            await CountReservationRequestsAsync(
                postgres.GetConnectionString(),
                cancellationToken));
    }

    private static ReserveInventoryHandler CreateHandler(
        InventoryDbContext dbContext, DateTimeOffset now)
    {
        return new ReserveInventoryHandler(
            new EfInventoryReservationStore(dbContext),
            new InventoryReservationPolicy(TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(now));
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
            .WithDatabase("switchyard_inventory_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
    }

    private static async Task ApplyMigrationsAsync(
        string connectionString, CancellationToken cancellationToken)
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
            """
            INSERT INTO inventory.stock_items (sku_code, on_hand_quantity, reserved_quantity)
            VALUES (@sku_code, @on_hand_quantity, 0);
            """,
            connection);

        command.Parameters.AddWithValue("sku_code", skuCode);
        command.Parameters.AddWithValue("on_hand_quantity", onHandQuantity);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReadReservedQuantityAsync(
        string connectionString, string skuCode, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT reserved_quantity
            FROM inventory.stock_items
            WHERE sku_code = @sku_code;
            """,
            connection);

        command.Parameters.AddWithValue("sku_code", skuCode);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountReservationRequestsAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM inventory.reservation_requests;",
            connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
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
