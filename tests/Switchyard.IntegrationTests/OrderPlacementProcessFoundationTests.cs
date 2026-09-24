using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OrderPlacementProcessFoundationTests
{
    private static readonly string[] ExpectedSkus =
        ["BIKE-001", "HELMET-001"];

    [Fact]
    public async Task CheckoutCommitsProcessAndPerLineReservationCommandsAtomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_order_placement_foundation_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();

        await using (var migrationContext = CreateDbContext(connectionString))
        {
            await migrationContext.Database.MigrateAsync(cancellationToken);
        }

        var now = new DateTimeOffset(
            2026, 9, 23, 10, 30, 0, TimeSpan.Zero);

        Guid orderId;

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var handler = new CreatePendingOrderHandler(
                new EfOrderRepository(dbContext),
                new EfOrderRequestRepository(dbContext),
                new EfOrderPlacementProcessRepository(dbContext),
                new EfOrderingUnitOfWork(dbContext),
                new EfOrderingOutboxStore(dbContext),
                new PostgresOrderNumberGenerator(dbContext),
                new FixedTimeProvider(now));

            var result = await handler.HandleAsync(
                new CreatePendingOrderCommand(
                    "checkout-process-foundation",
                    new[]
                    {
                        new CreatePendingOrderLine(
                            "BIKE-001", "Road Bike", 1, 1299.99m, "GBP"),
                        new CreatePendingOrderLine(
                            "HELMET-001", "Road Helmet", 2, 79.50m, "GBP")
                    }),
                cancellationToken);

            orderId = result.OrderId;
        }

        await using (var readContext = CreateDbContext(connectionString))
        {
            var process = await new EfOrderPlacementProcessRepository(readContext)
                .GetByOrderIdAsync(new OrderId(orderId), cancellationToken);

            Assert.NotNull(process);
            Assert.Equal(OrderPlacementState.AwaitingInventory, process!.State);
            Assert.Equal(now, process.StartedAtUtc);
            Assert.Equal(2, process.Lines.Count);
            Assert.All(
                process.Lines,
                line =>
                {
                    Assert.Equal(
                        OrderPlacementLineState.AwaitingReservation,
                        line.State);
                    Assert.NotEqual(Guid.Empty, line.ReservationRequestId);
                    Assert.Null(line.ReservationId);
                });
            Assert.Equal(
                2,
                process.Lines
                       .Select(line => line.ReservationRequestId)
                       .Distinct()
                       .Count());
        }

        var outbox = await ReadOutboxAsync(
            connectionString,
            cancellationToken);

        Assert.Equal(3, outbox.Count);

        var accepted = Assert.Single(
            outbox,
            message =>
                message.MessageType ==
                OrderAcceptedIntegrationMessageV1.MessageType);
        var reserveMessages = outbox
            .Where(message =>
                message.MessageType ==
                ReserveInventoryV1.MessageType)
            .ToArray();

        Assert.Equal(2, reserveMessages.Length);
        Assert.All(
            reserveMessages,
            message =>
            {
                Assert.Equal(orderId, message.CorrelationId);
                Assert.Equal(accepted.MessageId, message.CausationId);
            });

        var reservePayloads = reserveMessages
            .Select(message =>
                JsonSerializer.Deserialize<ReserveInventoryV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web))
            .Cast<ReserveInventoryV1>()
            .ToArray();

        Assert.Equal(
            ExpectedSkus,
            reservePayloads
                .Select(payload => payload.SkuCode)
                .OrderBy(value => value)
                .ToArray());
        Assert.Equal(
            2,
            reservePayloads
                .Select(payload => payload.RequestId)
                .Distinct()
                .Count());
    }

    private static OrderingDbContext CreateDbContext(
        string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderingDbContext(options);
    }

    private static async Task<IReadOnlyList<OutboxRow>> ReadOutboxAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var rows = new List<OutboxRow>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                message_id,
                message_type,
                payload::text,
                correlation_id,
                causation_id
            FROM ordering.outbox_messages
            ORDER BY message_type, message_id;
            """,
            connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new OutboxRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4)));
        }

        return rows;
    }

    private sealed record OutboxRow(
        Guid MessageId,
        string MessageType,
        string PayloadJson,
        Guid CorrelationId,
        Guid? CausationId);

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
