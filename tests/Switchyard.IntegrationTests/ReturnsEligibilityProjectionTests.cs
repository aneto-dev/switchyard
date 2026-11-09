using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Fulfilment;
using Switchyard.Messaging;
using Switchyard.Returns.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class ReturnsEligibilityProjectionTests
{
    [Fact]
    public async Task DeliveredShipmentBuildsQuantityAwareEligibilityAndReplayDoesNotDoubleCount()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_returns_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(cancellationToken);

        var connectionString = postgres.GetConnectionString();

        await using (var migrations = CreateDbContext(connectionString))
        {
            await migrations.Database.MigrateAsync(cancellationToken);
        }

        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var firstShipmentId = Guid.NewGuid();
        var secondShipmentId = Guid.NewGuid();

        var firstDeliveredAt =
            new DateTimeOffset(
                2026, 11, 9, 11, 30, 0, TimeSpan.Zero);

        var secondDeliveredAt =
            firstDeliveredAt.AddMinutes(30);

        var firstMessage =
            CreateMessage(
                orderId,
                firstShipmentId,
                lineId,
                1,
                firstDeliveredAt);

        var secondMessage =
            CreateMessage(
                orderId,
                secondShipmentId,
                lineId,
                2,
                secondDeliveredAt);

        var factory =
            new TestReturnsDbContextFactory(connectionString);

        var consumer =
            new ReturnsInboundMessageConsumer(
                factory,
                TimeProvider.System,
                new IReturnsInboundMessageRoute[]
                {
                    new ShipmentDeliveredReturnsInboundMessageRoute()
                });

        await consumer.ConsumeAsync(
            firstMessage,
            cancellationToken);

        await consumer.ConsumeAsync(
            firstMessage,
            cancellationToken);

        await consumer.ConsumeAsync(
            secondMessage,
            cancellationToken);

        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    delivered_quantity,
                    returned_quantity,
                    delivered_quantity - returned_quantity AS returnable_quantity,
                    (
                        SELECT COUNT(*)
                        FROM returns.shipment_deliveries
                        WHERE order_id = @order_id
                    ) AS shipment_count,
                    (
                        SELECT COUNT(*)
                        FROM returns.inbox_messages
                        WHERE correlation_id = @order_id
                    ) AS inbox_count
                FROM returns.return_eligibility_lines
                WHERE order_id = @order_id
                  AND order_line_id = @line_id;
                """,
                connection);

        command.Parameters.AddWithValue("order_id", orderId);
        command.Parameters.AddWithValue("line_id", lineId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(3, reader.GetInt32(2));
        Assert.Equal(2L, reader.GetInt64(3));
        Assert.Equal(2L, reader.GetInt64(4));
    }

    private static IntegrationMessageEnvelope CreateMessage(
        Guid orderId,
        Guid shipmentId,
        Guid lineId,
        int quantity,
        DateTimeOffset deliveredAtUtc)
    {
        var payload =
            new ShipmentDeliveredV1(
                orderId,
                shipmentId,
                "SW-RET-001",
                new[]
                {
                    new ShipmentLineV1(
                        lineId,
                        quantity)
                },
                deliveredAtUtc);

        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            ShipmentDeliveredV1.MessageType,
            JsonSerializer.Serialize(
                payload,
                JsonSerializerOptions.Web),
            deliveredAtUtc,
            orderId,
            Guid.NewGuid());
    }

    private static ReturnsDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<ReturnsDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new ReturnsDbContext(options);
    }

    private sealed class TestReturnsDbContextFactory :
        IDbContextFactory<ReturnsDbContext>
    {
        private readonly string _connectionString;

        public TestReturnsDbContextFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public ReturnsDbContext CreateDbContext() =>
            ReturnsEligibilityProjectionTests.CreateDbContext(_connectionString);

        public Task<ReturnsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ReturnsEligibilityProjectionTests.CreateDbContext(
                    _connectionString));
    }
}
