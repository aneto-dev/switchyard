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

public sealed class InventoryInboundMessageConsumerTests
{
    [Fact]
    public async Task ReserveCommandCommitsReservationInboxAndReservedEventAtomically()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        await SeedStockAsync(
            connectionString,
            "BIKE-001",
            2,
            cancellationToken);

        var now =
            new DateTimeOffset(
                2026,
                9,
                23,
                20,
                30,
                0,
                TimeSpan.Zero);

        var command =
            new ReserveInventoryV1(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "BIKE-001",
                1);

        var incoming =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                ReserveInventoryV1.MessageType,
                JsonSerializer.Serialize(
                    command,
                    JsonSerializerOptions.Web),
                now,
                command.OrderId,
                Guid.NewGuid());

        var consumer =
            CreateConsumer(
                connectionString,
                now);

        await consumer.ConsumeAsync(
            incoming,
            cancellationToken);

        await consumer.ConsumeAsync(
            incoming,
            cancellationToken);

        Assert.Equal(
            1,
            await ReadReservedQuantityAsync(
                connectionString,
                "BIKE-001",
                cancellationToken));

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "inventory.reservation_requests",
                cancellationToken));

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "inventory.inbox_messages",
                cancellationToken));

        var outbox =
            await ReadSingleOutboxAsync(
                connectionString,
                cancellationToken);

        Assert.Equal(
            InventoryReservedV1.MessageType,
            outbox.MessageType);

        Assert.Equal(
            command.OrderId,
            outbox.CorrelationId);

        Assert.Equal(
            incoming.MessageId,
            outbox.CausationId);

        var payload =
            JsonSerializer.Deserialize<InventoryReservedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(command.RequestId, payload.RequestId);
        Assert.Equal(command.OrderId, payload.OrderId);
        Assert.Equal(command.OrderLineId, payload.OrderLineId);
        Assert.Equal(command.SkuCode, payload.SkuCode);
        Assert.Equal(command.Quantity, payload.Quantity);
        Assert.NotEqual(Guid.Empty, payload.ReservationId);
        Assert.Equal(now, payload.ReservedAtUtc);
        Assert.Equal(now.AddMinutes(15), payload.ExpiresAtUtc);
    }

    [Fact]
    public async Task InsufficientStockCommitsInboxAndRejectedEvent()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var now =
            new DateTimeOffset(
                2026,
                9,
                23,
                20,
                45,
                0,
                TimeSpan.Zero);

        var command =
            new ReserveInventoryV1(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "MISSING-001",
                1);

        var incoming =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                ReserveInventoryV1.MessageType,
                JsonSerializer.Serialize(
                    command,
                    JsonSerializerOptions.Web),
                now,
                command.OrderId,
                Guid.NewGuid());

        var consumer =
            CreateConsumer(
                connectionString,
                now);

        await consumer.ConsumeAsync(
            incoming,
            cancellationToken);

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "inventory.reservation_requests",
                cancellationToken));

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "inventory.inbox_messages",
                cancellationToken));

        var outbox =
            await ReadSingleOutboxAsync(
                connectionString,
                cancellationToken);

        Assert.Equal(
            InventoryRejectedV1.MessageType,
            outbox.MessageType);

        Assert.Equal(
            command.OrderId,
            outbox.CorrelationId);

        Assert.Equal(
            incoming.MessageId,
            outbox.CausationId);

        var payload =
            JsonSerializer.Deserialize<InventoryRejectedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(command.RequestId, payload.RequestId);
        Assert.Equal(command.OrderLineId, payload.OrderLineId);
        Assert.Equal(
            InventoryRejectedV1.InsufficientStockReason,
            payload.Reason);
        Assert.Equal(now, payload.RejectedAtUtc);
    }

    private static InventoryInboundMessageConsumer CreateConsumer(
        string connectionString,
        DateTimeOffset now)
    {
        var timeProvider =
            new FixedTimeProvider(now);

        var route =
            new ReserveInventoryInboundMessageRoute(
                new InventoryReservationPolicy(
                    TimeSpan.FromMinutes(15)),
                timeProvider);

        return new InventoryInboundMessageConsumer(
            new TestInventoryDbContextFactory(
                connectionString),
            timeProvider,
            new[] { route });
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase(
                    "switchyard_inventory_messaging_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        return postgres;
    }

    private static InventoryDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new InventoryDbContext(options);
    }

    private static async Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(connectionString);

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }

    private static async Task SeedStockAsync(
        string connectionString,
        string skuCode,
        int onHandQuantity,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                INSERT INTO inventory.stock_items
                    (sku_code, on_hand_quantity, reserved_quantity)
                VALUES
                    (@sku_code, @on_hand_quantity, 0);
                """,
                connection);

        command.Parameters.AddWithValue(
            "sku_code",
            skuCode);

        command.Parameters.AddWithValue(
            "on_hand_quantity",
            onHandQuantity);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task<int> ReadReservedQuantityAsync(
        string connectionString,
        string skuCode,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT reserved_quantity
                FROM inventory.stock_items
                WHERE sku_code = @sku_code;
                """,
                connection);

        command.Parameters.AddWithValue(
            "sku_code",
            skuCode);

        var value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountRowsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                $"SELECT count(*) FROM {tableName};",
                connection);

        var value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<OutboxRow> ReadSingleOutboxAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    message_type,
                    payload::text,
                    correlation_id,
                    causation_id
                FROM inventory.outbox_messages;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        var row =
            new OutboxRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3)
                    ? null
                    : reader.GetGuid(3));

        Assert.False(
            await reader.ReadAsync(
                cancellationToken));

        return row;
    }

    private sealed record OutboxRow(
        string MessageType,
        string PayloadJson,
        Guid CorrelationId,
        Guid? CausationId);

    private sealed class TestInventoryDbContextFactory :
        IDbContextFactory<InventoryDbContext>
    {
        private readonly string _connectionString;

        public TestInventoryDbContextFactory(
            string connectionString)
        {
            _connectionString = connectionString;
        }

        public InventoryDbContext CreateDbContext() =>
            InventoryInboundMessageConsumerTests.CreateDbContext(
                _connectionString);

        public Task<InventoryDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;
    }
}
