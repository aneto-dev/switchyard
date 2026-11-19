using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class ReturnRefundOutcomeProjectionTests
{
    [Fact]
    public async Task RefundOutcomeProjectionIsTerminalAndOrderTolerant()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_return_refund_outcome_test")
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
        var returnRequestId = Guid.NewGuid();
        var returnReceiptId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var requestedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 17, 30, 0, TimeSpan.Zero);

        await SeedPendingRefundAsync(
            connectionString,
            orderId,
            returnRequestId,
            returnReceiptId,
            paymentId,
            requestedAtUtc,
            cancellationToken);

        var consumer =
            new ReturnsInboundMessageConsumer(
                new TestReturnsDbContextFactory(
                    connectionString),
                TimeProvider.System,
                new IReturnsInboundMessageRoute[]
                {
                    new PaymentRefundedReturnsInboundMessageRoute(),
                    new PaymentRefundIndeterminateReturnsInboundMessageRoute(),
                    new PaymentRefundNotAppliedReturnsInboundMessageRoute()
                });

        var providerReference =
            $"sim-refund-{Guid.NewGuid():N}";

        var indeterminateAtUtc =
            requestedAtUtc.AddMinutes(1);

        await consumer.ConsumeAsync(
            CreateEnvelope(
                new PaymentRefundIndeterminateV1(
                    returnReceiptId,
                    orderId,
                    paymentId,
                    returnRequestId,
                    returnReceiptId,
                    40m,
                    "GBP",
                    providerReference,
                    indeterminateAtUtc),
                PaymentRefundIndeterminateV1.MessageType,
                orderId,
                returnReceiptId,
                indeterminateAtUtc),
            cancellationToken);

        var succeededAtUtc =
            requestedAtUtc.AddMinutes(2);

        await consumer.ConsumeAsync(
            CreateEnvelope(
                new PaymentRefundedV1(
                    returnReceiptId,
                    orderId,
                    paymentId,
                    returnRequestId,
                    returnReceiptId,
                    40m,
                    "GBP",
                    providerReference,
                    succeededAtUtc),
                PaymentRefundedV1.MessageType,
                orderId,
                returnReceiptId,
                succeededAtUtc),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateEnvelope(
                new PaymentRefundIndeterminateV1(
                    returnReceiptId,
                    orderId,
                    paymentId,
                    returnRequestId,
                    returnReceiptId,
                    40m,
                    "GBP",
                    providerReference,
                    indeterminateAtUtc),
                PaymentRefundIndeterminateV1.MessageType,
                orderId,
                returnReceiptId,
                indeterminateAtUtc),
            cancellationToken);

        var contradictoryAtUtc =
            requestedAtUtc.AddMinutes(3);

        await Assert.ThrowsAsync<
            NonRetryableIntegrationMessageException>(
            () =>
                consumer.ConsumeAsync(
                    CreateEnvelope(
                        new PaymentRefundNotAppliedV1(
                            returnReceiptId,
                            orderId,
                            paymentId,
                            returnRequestId,
                            returnReceiptId,
                            40m,
                            "GBP",
                            providerReference,
                            contradictoryAtUtc),
                        PaymentRefundNotAppliedV1.MessageType,
                        orderId,
                        returnReceiptId,
                        contradictoryAtUtc),
                    cancellationToken));

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    status,
                    provider_reference,
                    updated_at_utc,
                    (
                        SELECT COUNT(*)
                        FROM returns.inbox_messages
                        WHERE correlation_id = @order_id
                          AND message_type LIKE 'payments.event.refund%'
                    ) AS processed_outcomes
                FROM returns.return_refunds
                WHERE order_id = @order_id
                  AND return_receipt_id = @return_receipt_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "return_receipt_id",
            returnReceiptId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(
            (int)ReturnRefundStatus.Succeeded,
            reader.GetInt32(0));

        Assert.Equal(
            providerReference,
            reader.GetString(1));

        Assert.Equal(
            succeededAtUtc,
            reader.GetFieldValue<DateTimeOffset>(2));

        Assert.Equal(
            3L,
            reader.GetInt64(3));
    }

    private static async Task SeedPendingRefundAsync(
        string connectionString,
        Guid orderId,
        Guid returnRequestId,
        Guid returnReceiptId,
        Guid paymentId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                INSERT INTO returns.return_eligibility
                (
                    order_id,
                    order_number
                )
                VALUES
                (
                    @order_id,
                    'SW-RET-600'
                );

                INSERT INTO returns.return_requests
                (
                    order_id,
                    return_request_id,
                    requested_at_utc,
                    status
                )
                VALUES
                (
                    @order_id,
                    @return_request_id,
                    @requested_at_utc,
                    2
                );

                INSERT INTO returns.return_receipts
                (
                    order_id,
                    return_receipt_id,
                    return_request_id,
                    received_at_utc
                )
                VALUES
                (
                    @order_id,
                    @return_receipt_id,
                    @return_request_id,
                    @requested_at_utc
                );

                INSERT INTO returns.return_refunds
                (
                    order_id,
                    return_receipt_id,
                    return_request_id,
                    payment_id,
                    amount,
                    currency,
                    status,
                    provider_reference,
                    requested_at_utc,
                    updated_at_utc
                )
                VALUES
                (
                    @order_id,
                    @return_receipt_id,
                    @return_request_id,
                    @payment_id,
                    40.00,
                    'GBP',
                    0,
                    NULL,
                    @requested_at_utc,
                    @requested_at_utc
                );
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "return_request_id",
            returnRequestId);

        command.Parameters.AddWithValue(
            "return_receipt_id",
            returnReceiptId);

        command.Parameters.AddWithValue(
            "payment_id",
            paymentId);

        command.Parameters.AddWithValue(
            "requested_at_utc",
            requestedAtUtc);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static IntegrationMessageEnvelope CreateEnvelope<T>(
        T payload,
        string messageType,
        Guid orderId,
        Guid requestId,
        DateTimeOffset occurredAtUtc)
        where T : class =>
        new(
            Guid.NewGuid(),
            messageType,
            JsonSerializer.Serialize(
                payload,
                JsonSerializerOptions.Web),
            occurredAtUtc,
            orderId,
            requestId);

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
            ReturnRefundOutcomeProjectionTests.CreateDbContext(
                _connectionString);

        public Task<ReturnsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ReturnRefundOutcomeProjectionTests.CreateDbContext(
                    _connectionString));
    }
}
