using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Fulfilment;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Commercial;
using Switchyard.Returns.Application.Receipts;
using Switchyard.Returns.Application.Requests;
using Switchyard.Returns.Domain.Eligibility;
using Switchyard.Returns.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class ReturnReceiptAcceptanceTests
{
    [Fact]
    public async Task PartialThenFinalReceiptPersistsQuantitiesStatusAndReplaySafety()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_return_receipt_test")
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
        var orderLineId = Guid.NewGuid();
        var returnRequestId = Guid.NewGuid();

        await ProjectDeliveryAsync(
            connectionString,
            orderId,
            orderLineId,
            quantity: 2,
            cancellationToken);

        await ProjectCommercialAsync(
            connectionString,
            orderId,
            orderLineId,
            unitPriceAmount: 75m,
            cancellationToken);

        await AuthoriseAsync(
            connectionString,
            returnRequestId,
            orderId,
            orderLineId,
            quantity: 2,
            cancellationToken);

        var firstReceiptId = Guid.NewGuid();
        var firstReceivedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

        var first =
            await ReceiveAsync(
                connectionString,
                firstReceiptId,
                returnRequestId,
                orderId,
                orderLineId,
                quantity: 1,
                firstReceivedAtUtc,
                cancellationToken);

        var replay =
            await ReceiveAsync(
                connectionString,
                firstReceiptId,
                returnRequestId,
                orderId,
                orderLineId,
                quantity: 1,
                firstReceivedAtUtc,
                cancellationToken);

        var final =
            await ReceiveAsync(
                connectionString,
                Guid.NewGuid(),
                returnRequestId,
                orderId,
                orderLineId,
                quantity: 1,
                firstReceivedAtUtc.AddMinutes(5),
                cancellationToken);

        Assert.False(first.Replayed);
        Assert.Equal(
            ReturnRequestStatus.PartiallyReceived,
            first.Status);

        Assert.True(replay.Replayed);
        Assert.Equal(
            ReturnRequestStatus.PartiallyReceived,
            replay.Status);

        Assert.False(final.Replayed);
        Assert.Equal(
            ReturnRequestStatus.Received,
            final.Status);

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    delivered_quantity,
                    authorised_return_quantity,
                    returned_quantity,
                    delivered_quantity - authorised_return_quantity - returned_quantity
                        AS returnable_quantity,
                    (
                        SELECT status
                        FROM returns.return_requests
                        WHERE order_id = @order_id
                          AND return_request_id = @return_request_id
                    ) AS request_status,
                    (
                        SELECT COUNT(*)
                        FROM returns.return_receipts
                        WHERE order_id = @order_id
                          AND return_request_id = @return_request_id
                    ) AS receipt_count,
                    (
                        SELECT COUNT(*)
                        FROM returns.outbox_messages
                        WHERE correlation_id = @order_id
                          AND message_type = 'payments.command.refund.v1'
                    ) AS refund_command_count,
                    (
                        SELECT SUM((payload ->> 'amount')::numeric)
                        FROM returns.outbox_messages
                        WHERE correlation_id = @order_id
                          AND message_type = 'payments.command.refund.v1'
                    ) AS refund_total
                FROM returns.return_eligibility_lines
                WHERE order_id = @order_id
                  AND order_line_id = @order_line_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "return_request_id",
            returnRequestId);

        command.Parameters.AddWithValue(
            "order_line_id",
            orderLineId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(2, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
        Assert.Equal(
            (int)ReturnRequestStatus.Received,
            reader.GetInt32(4));
        Assert.Equal(2L, reader.GetInt64(5));
        Assert.Equal(2L, reader.GetInt64(6));
        Assert.Equal(150m, reader.GetDecimal(7));
    }

    private static async Task ProjectCommercialAsync(
        string connectionString,
        Guid orderId,
        Guid orderLineId,
        decimal unitPriceAmount,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var repository =
            new EfReturnCommercialSnapshotRepository(
                dbContext);

        await repository.ApplyAsync(
            new ReturnCommercialSnapshot(
                orderId,
                Guid.NewGuid(),
                "SW-RET-400",
                unitPriceAmount * 2,
                "GBP",
                new DateTimeOffset(
                    2026, 11, 9, 11, 55, 0, TimeSpan.Zero),
                new[]
                {
                    new ReturnCommercialSnapshotLine(
                        orderLineId,
                        2,
                        unitPriceAmount,
                        "GBP")
                }),
            cancellationToken);
    }

    private static async Task ProjectDeliveryAsync(
        string connectionString,
        Guid orderId,
        Guid orderLineId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var deliveredAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        var payload =
            new ShipmentDeliveredV1(
                orderId,
                Guid.NewGuid(),
                "SW-RET-400",
                new[]
                {
                    new ShipmentLineV1(
                        orderLineId,
                        quantity)
                },
                deliveredAtUtc);

        var message =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                ShipmentDeliveredV1.MessageType,
                JsonSerializer.Serialize(
                    payload,
                    JsonSerializerOptions.Web),
                deliveredAtUtc,
                orderId,
                Guid.NewGuid());

        var consumer =
            new ReturnsInboundMessageConsumer(
                new TestReturnsDbContextFactory(
                    connectionString),
                TimeProvider.System,
                new IReturnsInboundMessageRoute[]
                {
                    new ShipmentDeliveredReturnsInboundMessageRoute()
                });

        await consumer.ConsumeAsync(
            message,
            cancellationToken);
    }

    private static async Task AuthoriseAsync(
        string connectionString,
        Guid returnRequestId,
        Guid orderId,
        Guid orderLineId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new AuthoriseReturnRequestHandler(
                new EfReturnEligibilityRepository(
                    dbContext),
                new EfReturnsUnitOfWork(
                    dbContext));

        await handler.HandleAsync(
            new AuthoriseReturnRequestCommand(
                returnRequestId,
                orderId,
                new[]
                {
                    new AuthoriseReturnRequestLine(
                        orderLineId,
                        quantity)
                },
                new DateTimeOffset(
                    2026, 11, 9, 12, 5, 0, TimeSpan.Zero)),
            cancellationToken);
    }

    private static async Task<ReceiveReturnResult> ReceiveAsync(
        string connectionString,
        Guid returnReceiptId,
        Guid returnRequestId,
        Guid orderId,
        Guid orderLineId,
        int quantity,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new ReceiveReturnHandler(
                new EfReturnEligibilityRepository(
                    dbContext),
                new EfReturnCommercialSnapshotRepository(
                    dbContext),
                new EfReturnsOutboxStore(
                    dbContext),
                new EfReturnRefundRepository(
                    dbContext),
                new EfReturnsUnitOfWork(
                    dbContext));

        return await handler.HandleAsync(
            new ReceiveReturnCommand(
                returnReceiptId,
                returnRequestId,
                orderId,
                new[]
                {
                    new ReceiveReturnLine(
                        orderLineId,
                        quantity)
                },
                receivedAtUtc),
            cancellationToken);
    }

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
            ReturnReceiptAcceptanceTests.CreateDbContext(
                _connectionString);

        public Task<ReturnsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ReturnReceiptAcceptanceTests.CreateDbContext(
                    _connectionString));
    }
}
