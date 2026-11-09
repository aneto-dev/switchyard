using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Ordering;
using Switchyard.Messaging;
using Switchyard.Returns.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class ReturnsCommercialSnapshotProjectionTests
{
    [Fact]
    public async Task CommercialSnapshotProjectsOnceAcrossLogicalReplay()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_returns_commercial_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await using (var migrations =
            CreateDbContext(connectionString))
        {
            await migrations.Database.MigrateAsync(
                cancellationToken);
        }

        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var bikeLineId = Guid.NewGuid();
        var helmetLineId = Guid.NewGuid();
        var confirmedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 14, 30, 0, TimeSpan.Zero);

        var snapshot =
            new OrderCommercialSnapshotV1(
                orderId,
                paymentId,
                "SW-RET-500",
                1399.98m,
                "GBP",
                confirmedAtUtc,
                new[]
                {
                    new OrderCommercialSnapshotLineV1(
                        bikeLineId,
                        1,
                        1299.99m,
                        "GBP"),
                    new OrderCommercialSnapshotLineV1(
                        helmetLineId,
                        1,
                        99.99m,
                        "GBP")
                });

        var consumer =
            new ReturnsInboundMessageConsumer(
                new TestReturnsDbContextFactory(
                    connectionString),
                TimeProvider.System,
                new IReturnsInboundMessageRoute[]
                {
                    new OrderCommercialSnapshotReturnsInboundMessageRoute()
                });

        await consumer.ConsumeAsync(
            CreateEnvelope(
                snapshot,
                Guid.NewGuid()),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateEnvelope(
                snapshot,
                Guid.NewGuid()),
            cancellationToken);

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    payment_id,
                    total_amount,
                    currency,
                    (
                        SELECT COUNT(*)
                        FROM returns.commercial_snapshot_lines
                        WHERE order_id = @order_id
                    ) AS line_count,
                    (
                        SELECT SUM(quantity * unit_price_amount)
                        FROM returns.commercial_snapshot_lines
                        WHERE order_id = @order_id
                    ) AS line_total,
                    (
                        SELECT COUNT(*)
                        FROM returns.inbox_messages
                        WHERE correlation_id = @order_id
                          AND message_type = @message_type
                    ) AS inbox_count
                FROM returns.commercial_snapshots
                WHERE order_id = @order_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "message_type",
            OrderCommercialSnapshotV1.MessageType);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(
            paymentId,
            reader.GetGuid(0));

        Assert.Equal(
            1399.98m,
            reader.GetDecimal(1));

        Assert.Equal(
            "GBP",
            reader.GetString(2));

        Assert.Equal(
            2L,
            reader.GetInt64(3));

        Assert.Equal(
            1399.98m,
            reader.GetDecimal(4));

        Assert.Equal(
            2L,
            reader.GetInt64(5));
    }

    [Fact]
    public async Task ConflictingCommercialSnapshotIsRejected()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_returns_commercial_conflict_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await using (var migrations =
            CreateDbContext(connectionString))
        {
            await migrations.Database.MigrateAsync(
                cancellationToken);
        }

        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var confirmedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 14, 45, 0, TimeSpan.Zero);

        var first =
            new OrderCommercialSnapshotV1(
                orderId,
                paymentId,
                "SW-RET-501",
                100m,
                "GBP",
                confirmedAtUtc,
                new[]
                {
                    new OrderCommercialSnapshotLineV1(
                        orderLineId,
                        1,
                        100m,
                        "GBP")
                });

        var conflicting =
            first with
            {
                TotalAmount = 120m,
                Lines =
                    new[]
                    {
                        new OrderCommercialSnapshotLineV1(
                            orderLineId,
                            1,
                            120m,
                            "GBP")
                    }
            };

        var consumer =
            new ReturnsInboundMessageConsumer(
                new TestReturnsDbContextFactory(
                    connectionString),
                TimeProvider.System,
                new IReturnsInboundMessageRoute[]
                {
                    new OrderCommercialSnapshotReturnsInboundMessageRoute()
                });

        await consumer.ConsumeAsync(
            CreateEnvelope(
                first,
                Guid.NewGuid()),
            cancellationToken);

        await Assert.ThrowsAsync<NonRetryableIntegrationMessageException>(
            () =>
                consumer.ConsumeAsync(
                    CreateEnvelope(
                        conflicting,
                        Guid.NewGuid()),
                    cancellationToken));
    }

    private static IntegrationMessageEnvelope CreateEnvelope(
        OrderCommercialSnapshotV1 snapshot,
        Guid messageId) =>
        new(
            messageId,
            OrderCommercialSnapshotV1.MessageType,
            JsonSerializer.Serialize(
                snapshot,
                JsonSerializerOptions.Web),
            snapshot.ConfirmedAtUtc,
            snapshot.OrderId,
            Guid.NewGuid());

    private static ReturnsDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<ReturnsDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new ReturnsDbContext(
            options);
    }

    private sealed class TestReturnsDbContextFactory :
        IDbContextFactory<ReturnsDbContext>
    {
        private readonly string _connectionString;

        public TestReturnsDbContextFactory(
            string connectionString)
        {
            _connectionString =
                connectionString;
        }

        public ReturnsDbContext CreateDbContext() =>
            ReturnsCommercialSnapshotProjectionTests.CreateDbContext(
                _connectionString);

        public Task<ReturnsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ReturnsCommercialSnapshotProjectionTests.CreateDbContext(
                    _connectionString));
    }
}
