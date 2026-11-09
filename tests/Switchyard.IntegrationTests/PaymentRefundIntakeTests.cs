using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Refunds;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentRefundIntakeTests
{
    [Fact]
    public async Task CapturedPaymentAcceptsRefundReplayAndRejectsCumulativeOverRefund()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_payment_refund_test")
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

        var now =
            new DateTimeOffset(
                2026, 11, 9, 15, 30, 0, TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);

        Guid paymentId;
        var orderId = Guid.NewGuid();

        await using (var setupContext =
            CreateDbContext(
                connectionString))
        {
            var authorisation =
                await new AuthorisePaymentHandler(
                    new EfPaymentAuthorisationStore(
                        setupContext),
                    provider,
                    new FixedTimeProvider(now))
                    .HandleAsync(
                        new AuthorisePaymentCommand(
                            Guid.NewGuid(),
                            orderId,
                            100m,
                            "GBP"),
                        cancellationToken);

            Assert.Equal(
                PaymentAuthorisationStatus.Authorised,
                authorisation.Status);

            paymentId =
                authorisation.PaymentId;

            var capture =
                await new ExecutePaymentSettlementHandler(
                    new EfPaymentSettlementStore(
                        setupContext),
                    provider,
                    new FixedTimeProvider(
                        now.AddMinutes(1)))
                    .HandleAsync(
                        new ExecutePaymentSettlementCommand(
                            Guid.NewGuid(),
                            paymentId,
                            PaymentSettlementAction.Capture),
                        cancellationToken);

            Assert.Equal(
                PaymentSettlementStatus.Succeeded,
                capture.Status);
        }

        var consumer =
            new PaymentsInboundMessageConsumer(
                new TestPaymentsDbContextFactory(
                    connectionString),
                provider,
                provider,
                new FixedTimeProvider(
                    now.AddMinutes(2)));

        var firstRequestId = Guid.NewGuid();

        var first =
            new RefundPaymentV1(
                firstRequestId,
                orderId,
                paymentId,
                Guid.NewGuid(),
                firstRequestId,
                40m,
                "GBP");

        await consumer.ConsumeAsync(
            CreateEnvelope(
                first,
                Guid.NewGuid(),
                now.AddMinutes(2)),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateEnvelope(
                first,
                Guid.NewGuid(),
                now.AddMinutes(2)),
            cancellationToken);

        var secondRequestId =
            Guid.NewGuid();

        var excessive =
            new RefundPaymentV1(
                secondRequestId,
                orderId,
                paymentId,
                Guid.NewGuid(),
                secondRequestId,
                70m,
                "GBP");

        await Assert.ThrowsAsync<
            NonRetryableIntegrationMessageException>(
            () =>
                consumer.ConsumeAsync(
                    CreateEnvelope(
                        excessive,
                        Guid.NewGuid(),
                        now.AddMinutes(3)),
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
                    COUNT(*),
                    SUM(amount),
                    MIN(status),
                    (
                        SELECT COUNT(*)
                        FROM payments.inbox_messages
                        WHERE consumer_name = 'payments-refund'
                          AND processed_at_utc IS NOT NULL
                    ) AS processed_inbox
                FROM payments.refund_requests
                WHERE payment_id = @payment_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "payment_id",
            paymentId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(
            1L,
            reader.GetInt64(0));

        Assert.Equal(
            40m,
            reader.GetDecimal(1));

        Assert.Equal(
            (int)PaymentRefundStatus.Pending,
            reader.GetInt32(2));

        Assert.Equal(
            2L,
            reader.GetInt64(3));
    }

    private static IntegrationMessageEnvelope CreateEnvelope(
        RefundPaymentV1 command,
        Guid messageId,
        DateTimeOffset occurredAtUtc) =>
        new(
            messageId,
            RefundPaymentV1.MessageType,
            JsonSerializer.Serialize(
                command,
                JsonSerializerOptions.Web),
            occurredAtUtc,
            command.OrderId,
            command.ReturnReceiptId);

    private static PaymentsDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        return new PaymentsDbContext(
            options);
    }

    private sealed class TestPaymentsDbContextFactory :
        IDbContextFactory<PaymentsDbContext>
    {
        private readonly string _connectionString;

        public TestPaymentsDbContextFactory(
            string connectionString)
        {
            _connectionString =
                connectionString;
        }

        public PaymentsDbContext CreateDbContext() =>
            PaymentRefundIntakeTests.CreateDbContext(
                _connectionString);

        public Task<PaymentsDbContext> CreateDbContextAsync(
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
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;
    }
}
