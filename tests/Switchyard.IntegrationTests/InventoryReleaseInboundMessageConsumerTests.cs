using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class InventoryReleaseInboundMessageConsumerTests
{
    [Fact]
    public async Task ReleaseCommandCommitsReleaseInboxAndReleasedEventAtomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-RELEASE-MSG", 1, cancellationToken);

        var reservedAt = new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
        var reservation = await ReserveDirectAsync(connectionString, "BIKE-RELEASE-MSG", reservedAt, cancellationToken);
        var releasedAt = reservedAt.AddMinutes(1);
        var incoming = CreateReleaseEnvelope(reservation, releasedAt);
        var consumer = CreateConsumer(connectionString, releasedAt);

        await consumer.ConsumeAsync(incoming, cancellationToken);
        await consumer.ConsumeAsync(incoming, cancellationToken);

        Assert.Equal(0, await ReadReservedQuantityAsync(connectionString, "BIKE-RELEASE-MSG", cancellationToken));
        Assert.Equal(1, await CountRowsAsync(connectionString, "inventory.inbox_messages", cancellationToken));

        var outbox = await ReadSingleOutboxAsync(connectionString, cancellationToken);
        Assert.Equal(InventoryReleasedV1.MessageType, outbox.MessageType);
        Assert.Equal(reservation.OrderId, outbox.CorrelationId);
        Assert.Equal(incoming.MessageId, outbox.CausationId);

        var payload = JsonSerializer.Deserialize<InventoryReleasedV1>(
            outbox.PayloadJson,
            JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(reservation.RequestId, payload.RequestId);
        Assert.Equal(reservation.OrderId, payload.OrderId);
        Assert.Equal(reservation.ReservationId, payload.ReservationId);
        Assert.Equal(ReleaseInventoryReservationV1.CompensationReason, payload.Reason);
        Assert.Equal(releasedAt, payload.ReleasedAtUtc);
    }

    [Fact]
    public async Task ReleaseCommandForExpiredReservationEmitsActualExpiredFact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-EXPIRED-MSG", 1, cancellationToken);

        var reservedAt = new DateTimeOffset(2026, 9, 27, 18, 10, 0, TimeSpan.Zero);
        var reservation = await ReserveDirectAsync(connectionString, "BIKE-EXPIRED-MSG", reservedAt, cancellationToken);
        var expiredAt = reservedAt.AddMinutes(16);

        await using (var expiryContext = CreateDbContext(connectionString))
        {
            var store = new EfInventoryReservationLifecycleStore(expiryContext);
            Assert.Equal(1, await store.ExpireAsync(expiredAt, 10, cancellationToken));
        }

        var observedAt = expiredAt.AddMinutes(1);
        await CreateConsumer(connectionString, observedAt)
            .ConsumeAsync(CreateReleaseEnvelope(reservation, observedAt), cancellationToken);

        var outbox = await ReadSingleOutboxAsync(connectionString, cancellationToken);
        Assert.Equal(InventoryExpiredV1.MessageType, outbox.MessageType);

        var payload = JsonSerializer.Deserialize<InventoryExpiredV1>(
            outbox.PayloadJson,
            JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(reservation.RequestId, payload.RequestId);
        Assert.Equal(reservation.OrderId, payload.OrderId);
        Assert.Equal(reservation.ReservationId, payload.ReservationId);
        Assert.Equal(expiredAt, payload.ExpiredAtUtc);
    }

    [Fact]
    public async Task ReleaseCommandWithWrongReservationIdentityIsNonRetryable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await SeedStockAsync(connectionString, "BIKE-CONFLICT-MSG", 1, cancellationToken);

        var now = new DateTimeOffset(2026, 9, 27, 18, 20, 0, TimeSpan.Zero);
        var reservation = await ReserveDirectAsync(connectionString, "BIKE-CONFLICT-MSG", now, cancellationToken);
        var conflicting = reservation with { RequestId = Guid.NewGuid() };
        var incoming = CreateReleaseEnvelope(conflicting, now.AddMinutes(1));

        await Assert.ThrowsAsync<NonRetryableIntegrationMessageException>(
            () => CreateConsumer(connectionString, now.AddMinutes(1))
                .ConsumeAsync(incoming, cancellationToken));

        Assert.Equal(1, await ReadReservedQuantityAsync(connectionString, "BIKE-CONFLICT-MSG", cancellationToken));
        Assert.Equal(0, await CountRowsAsync(connectionString, "inventory.inbox_messages", cancellationToken));
        Assert.Equal(0, await CountRowsAsync(connectionString, "inventory.outbox_messages", cancellationToken));
    }

    private static InventoryInboundMessageConsumer CreateConsumer(
        string connectionString,
        DateTimeOffset now)
    {
        var timeProvider = new FixedTimeProvider(now);
        return new InventoryInboundMessageConsumer(
            new TestInventoryDbContextFactory(connectionString),
            timeProvider,
            new IInventoryInboundMessageRoute[]
            {
                new ReleaseInventoryInboundMessageRoute(timeProvider)
            });
    }

    private static IntegrationMessageEnvelope CreateReleaseEnvelope(
        ReservationIdentity reservation,
        DateTimeOffset occurredAtUtc)
    {
        var command = new ReleaseInventoryReservationV1(
            reservation.RequestId,
            reservation.OrderId,
            reservation.ReservationId,
            ReleaseInventoryReservationV1.CompensationReason);

        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            ReleaseInventoryReservationV1.MessageType,
            JsonSerializer.Serialize(command, JsonSerializerOptions.Web),
            occurredAtUtc,
            reservation.OrderId,
            Guid.NewGuid());
    }

    private static async Task<ReservationIdentity> ReserveDirectAsync(
        string connectionString,
        string skuCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(connectionString);
        var result = await new ReserveInventoryHandler(
            new EfInventoryReservationStore(dbContext),
            new InventoryReservationPolicy(TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(now))
            .HandleAsync(
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
            .WithDatabase("switchyard_inventory_release_messaging_test")
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
                (@sku_code, @on_hand_quantity, 0);
            """,
            connection);
        command.Parameters.AddWithValue("sku_code", skuCode);
        command.Parameters.AddWithValue("on_hand_quantity", onHandQuantity);
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
            WHERE sku_code = @sku_code;
            """,
            connection);
        command.Parameters.AddWithValue("sku_code", skuCode);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
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

    private static async Task<OutboxRow> ReadSingleOutboxAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT message_type, payload::text, correlation_id, causation_id
            FROM inventory.outbox_messages;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        var row = new OutboxRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
        Assert.False(await reader.ReadAsync(cancellationToken));
        return row;
    }

    private sealed record ReservationIdentity(Guid RequestId, Guid OrderId, Guid ReservationId);
    private sealed record OutboxRow(string MessageType, string PayloadJson, Guid CorrelationId, Guid? CausationId);

    private sealed class TestInventoryDbContextFactory : IDbContextFactory<InventoryDbContext>
    {
        private readonly string _connectionString;
        public TestInventoryDbContextFactory(string connectionString) { _connectionString = connectionString; }
        public InventoryDbContext CreateDbContext() => InventoryReleaseInboundMessageConsumerTests.CreateDbContext(_connectionString);
        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) { _utcNow = utcNow; }
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
