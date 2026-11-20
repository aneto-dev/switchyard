using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Domain.Eligibility;
using Switchyard.Returns.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OperationsReturnsQueriesTests
{
    private static readonly DateTimeOffset DeliveredAtUtc = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadsReturnRequestsReceiptsAndRefundsForOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_operations_returns_test")
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
        var deliveredOnlyOrderId = Guid.NewGuid();
        var bikeLineId = Guid.NewGuid();
        var helmetLineId = Guid.NewGuid();
        var firstRequestId = Guid.NewGuid();
        var secondRequestId = Guid.NewGuid();
        var refundedReceiptId = Guid.NewGuid();
        var pendingReceiptId = Guid.NewGuid();
        var unvaluedReceiptId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Bike: 3 delivered, 2 returned over two receipts, 1 more authorised and awaiting receipt.
        // Helmet: 1 delivered and returned, not yet valued for refund.
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO returns.return_eligibility (order_id, order_number) VALUES
                (@order_id, 'SW-00100090'),
                (@delivered_only_order_id, 'SW-00100091');

            INSERT INTO returns.return_eligibility_lines
                (order_id, order_line_id, delivered_quantity, authorised_return_quantity, returned_quantity)
            VALUES
                (@order_id, @bike_line_id, 3, 1, 2),
                (@order_id, @helmet_line_id, 1, 0, 1),
                (@delivered_only_order_id, @bike_line_id, 1, 0, 0);

            INSERT INTO returns.return_requests (order_id, return_request_id, requested_at_utc, status) VALUES
                (@order_id, @first_request_id, @t1, 2),
                (@order_id, @second_request_id, @t2, 2);

            INSERT INTO returns.return_request_lines (order_id, return_request_id, order_line_id, quantity) VALUES
                (@order_id, @first_request_id, @bike_line_id, 2),
                (@order_id, @second_request_id, @helmet_line_id, 1);

            INSERT INTO returns.return_receipts (order_id, return_receipt_id, return_request_id, received_at_utc) VALUES
                (@order_id, @refunded_receipt_id, @first_request_id, @t3),
                (@order_id, @pending_receipt_id, @first_request_id, @t4),
                (@order_id, @unvalued_receipt_id, @second_request_id, @t5);

            INSERT INTO returns.return_receipt_lines (order_id, return_receipt_id, order_line_id, quantity) VALUES
                (@order_id, @refunded_receipt_id, @bike_line_id, 1),
                (@order_id, @pending_receipt_id, @bike_line_id, 1),
                (@order_id, @unvalued_receipt_id, @helmet_line_id, 1);

            INSERT INTO returns.return_refunds
                (order_id, return_receipt_id, return_request_id, payment_id, amount, currency, status,
                 provider_reference, requested_at_utc, updated_at_utc)
            VALUES
                (@order_id, @refunded_receipt_id, @first_request_id, @payment_id, 1299.99, 'GBP', 2,
                 'sim-refund-001', @t3, @t4),
                (@order_id, @pending_receipt_id, @first_request_id, @payment_id, 1299.99, 'GBP', 0,
                 NULL, @t4, @t4);
            """,
            new Dictionary<string, object>
            {
                ["order_id"] = orderId,
                ["delivered_only_order_id"] = deliveredOnlyOrderId,
                ["bike_line_id"] = bikeLineId,
                ["helmet_line_id"] = helmetLineId,
                ["first_request_id"] = firstRequestId,
                ["second_request_id"] = secondRequestId,
                ["refunded_receipt_id"] = refundedReceiptId,
                ["pending_receipt_id"] = pendingReceiptId,
                ["unvalued_receipt_id"] = unvaluedReceiptId,
                ["payment_id"] = paymentId,
                ["t1"] = DeliveredAtUtc.AddDays(1),
                ["t2"] = DeliveredAtUtc.AddDays(2),
                ["t3"] = DeliveredAtUtc.AddDays(3),
                ["t4"] = DeliveredAtUtc.AddDays(4),
                ["t5"] = DeliveredAtUtc.AddDays(5)
            },
            cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var queries = new EfOperationsReturnsQueries(dbContext);

        var view = await queries.GetByOrderIdAsync(orderId, cancellationToken);

        Assert.NotNull(view);
        Assert.Equal(orderId, view.OrderId);

        var bike = Assert.Single(view.Lines, line => line.OrderLineId == bikeLineId);
        Assert.Equal(3, bike.DeliveredQuantity);
        Assert.Equal(1, bike.AuthorisedReturnQuantity);
        Assert.Equal(2, bike.ReturnedQuantity);

        Assert.Collection(
            view.Requests,
            first =>
            {
                Assert.Equal(firstRequestId, first.ReturnRequestId);
                Assert.Equal(ReturnRequestStatus.Received, first.Status);
                Assert.Equal(2, first.Quantity);
                Assert.Equal(DeliveredAtUtc.AddDays(1), first.RequestedAtUtc);
                Assert.Collection(
                    first.Receipts,
                    refunded =>
                    {
                        Assert.Equal(refundedReceiptId, refunded.ReturnReceiptId);
                        Assert.Equal(1, refunded.Quantity);
                        Assert.NotNull(refunded.Refund);
                        Assert.Equal(ReturnRefundStatus.Succeeded, refunded.Refund.Status);
                        Assert.Equal(1299.99m, refunded.Refund.Amount);
                        Assert.Equal("sim-refund-001", refunded.Refund.ProviderReference);
                        Assert.Equal(DeliveredAtUtc.AddDays(4), refunded.Refund.UpdatedAtUtc);
                    },
                    pending =>
                    {
                        Assert.Equal(pendingReceiptId, pending.ReturnReceiptId);
                        Assert.NotNull(pending.Refund);
                        Assert.Equal(ReturnRefundStatus.Pending, pending.Refund.Status);
                        Assert.Null(pending.Refund.ProviderReference);
                    });
            },
            second =>
            {
                Assert.Equal(secondRequestId, second.ReturnRequestId);
                var receipt = Assert.Single(second.Receipts);
                Assert.Equal(unvaluedReceiptId, receipt.ReturnReceiptId);
                Assert.Equal(DeliveredAtUtc.AddDays(5), receipt.ReceivedAtUtc);
                Assert.Null(receipt.Refund);
            });

        var deliveredOnly = await queries.GetByOrderIdAsync(deliveredOnlyOrderId, cancellationToken);

        Assert.NotNull(deliveredOnly);
        Assert.Single(deliveredOnly.Lines);
        Assert.Empty(deliveredOnly.Requests);

        Assert.Null(await queries.GetByOrderIdAsync(Guid.NewGuid(), cancellationToken));
        Assert.Null(await queries.GetByOrderIdAsync(Guid.Empty, cancellationToken));
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ReturnsDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ReturnsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ReturnsDbContext(options);
    }
}
