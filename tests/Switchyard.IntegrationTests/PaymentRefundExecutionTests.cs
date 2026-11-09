using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Refunds;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Refunds;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentRefundExecutionTests
{
    [Fact]
    public async Task PendingRefundExecutesOnceAndStagesSucceededOutcome()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_refund_execution_test")
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

        var requestedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

        var setup =
            await CreateCapturedRefundAsync(
                connectionString,
                requestedAtUtc,
                refundAmount: 40m,
                cancellationToken);

        var refundProvider =
            new SimulatedPaymentRefundProvider(
                SimulatedPaymentRefundScenario.Succeed,
                SimulatedPaymentRefundReconciliationScenario.Succeed);

        var cycle =
            new PaymentsRefundExecutionCycle(
                new TestPaymentsDbContextFactory(
                    connectionString),
                refundProvider,
                new FixedTimeProvider(
                    requestedAtUtc.AddMinutes(2)),
                new PaymentsRefundExecutionOptions(
                    batchSize: 10,
                    pollInterval: TimeSpan.FromSeconds(1)),
                NullLogger<PaymentsRefundExecutionCycle>.Instance);

        var first =
            await cycle.ExecuteAsync(
                cancellationToken);

        var second =
            await cycle.ExecuteAsync(
                cancellationToken);

        Assert.Equal(1, first.Due);
        Assert.Equal(1, first.Applied);
        Assert.Equal(1, first.Succeeded);
        Assert.Equal(0, first.Indeterminate);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, second.Due);
        Assert.Equal(1, refundProvider.InvocationCount);
        Assert.Equal(1, refundProvider.UniqueRefundCount);

        await AssertRefundStateAsync(
            connectionString,
            setup.RefundRequestId,
            PaymentRefundStatus.Succeeded,
            expectedReconciliationAttempts: 0,
            cancellationToken);

        var outcome =
            await ReadSingleOutcomeAsync<PaymentRefundedV1>(
                connectionString,
                setup.OrderId,
                PaymentRefundedV1.MessageType,
                cancellationToken);

        Assert.Equal(
            setup.RefundRequestId,
            outcome.RequestId);

        Assert.Equal(
            setup.PaymentId,
            outcome.PaymentId);

        Assert.Equal(
            40m,
            outcome.Amount);
    }

    [Fact]
    public async Task IndeterminateRefundReconcilesWithoutSecondProviderRefund()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_refund_reconciliation_test")
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

        var requestedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 16, 30, 0, TimeSpan.Zero);

        var setup =
            await CreateCapturedRefundAsync(
                connectionString,
                requestedAtUtc,
                refundAmount: 55m,
                cancellationToken);

        var refundProvider =
            new SimulatedPaymentRefundProvider(
                SimulatedPaymentRefundScenario.Indeterminate,
                SimulatedPaymentRefundReconciliationScenario.Succeed);

        var executionCycle =
            new PaymentsRefundExecutionCycle(
                new TestPaymentsDbContextFactory(
                    connectionString),
                refundProvider,
                new FixedTimeProvider(
                    requestedAtUtc.AddMinutes(2)),
                new PaymentsRefundExecutionOptions(
                    batchSize: 10,
                    pollInterval: TimeSpan.FromSeconds(1)),
                NullLogger<PaymentsRefundExecutionCycle>.Instance);

        var execution =
            await executionCycle.ExecuteAsync(
                cancellationToken);

        Assert.Equal(1, execution.Applied);
        Assert.Equal(1, execution.Indeterminate);
        Assert.Equal(1, refundProvider.InvocationCount);

        await AssertRefundStateAsync(
            connectionString,
            setup.RefundRequestId,
            PaymentRefundStatus.Indeterminate,
            expectedReconciliationAttempts: 0,
            cancellationToken);

        var indeterminate =
            await ReadSingleOutcomeAsync<PaymentRefundIndeterminateV1>(
                connectionString,
                setup.OrderId,
                PaymentRefundIndeterminateV1.MessageType,
                cancellationToken);

        Assert.Equal(
            setup.RefundRequestId,
            indeterminate.RequestId);

        var reconciliationCycle =
            new PaymentsRefundReconciliationCycle(
                new TestPaymentsDbContextFactory(
                    connectionString),
                refundProvider,
                new FixedTimeProvider(
                    requestedAtUtc.AddMinutes(3)),
                new PaymentsRefundReconciliationOptions(
                    batchSize: 10,
                    maxAttempts: 3,
                    retryDelay: TimeSpan.Zero,
                    pollInterval: TimeSpan.FromSeconds(1)),
                NullLogger<PaymentsRefundReconciliationCycle>.Instance);

        var reconciled =
            await reconciliationCycle.ReconcileAsync(
                cancellationToken);

        var replay =
            await reconciliationCycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(1, reconciled.Due);
        Assert.Equal(1, reconciled.Applied);
        Assert.Equal(1, reconciled.Resolved);
        Assert.Equal(0, reconciled.Failed);
        Assert.Equal(0, replay.Due);

        Assert.Equal(1, refundProvider.InvocationCount);
        Assert.Equal(1, refundProvider.ReconciliationInvocationCount);

        await AssertRefundStateAsync(
            connectionString,
            setup.RefundRequestId,
            PaymentRefundStatus.Succeeded,
            expectedReconciliationAttempts: 1,
            cancellationToken);

        var refunded =
            await ReadSingleOutcomeAsync<PaymentRefundedV1>(
                connectionString,
                setup.OrderId,
                PaymentRefundedV1.MessageType,
                cancellationToken);

        Assert.Equal(
            setup.RefundRequestId,
            refunded.RequestId);

        Assert.Equal(
            55m,
            refunded.Amount);
    }

    private static async Task<RefundSetup> CreateCapturedRefundAsync(
        string connectionString,
        DateTimeOffset requestedAtUtc,
        decimal refundAmount,
        CancellationToken cancellationToken)
    {
        var orderId =
            Guid.NewGuid();

        var authorisationProvider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);

        Guid paymentId;

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var authorised =
                await new AuthorisePaymentHandler(
                    new EfPaymentAuthorisationStore(
                        dbContext),
                    authorisationProvider,
                    new FixedTimeProvider(
                        requestedAtUtc))
                    .HandleAsync(
                        new AuthorisePaymentCommand(
                            Guid.NewGuid(),
                            orderId,
                            100m,
                            "GBP"),
                        cancellationToken);

            paymentId =
                authorised.PaymentId;
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var captured =
                await new ExecutePaymentSettlementHandler(
                    new EfPaymentSettlementStore(
                        dbContext),
                    authorisationProvider,
                    new FixedTimeProvider(
                        requestedAtUtc.AddMinutes(1)))
                    .HandleAsync(
                        new ExecutePaymentSettlementCommand(
                            Guid.NewGuid(),
                            paymentId,
                            PaymentSettlementAction.Capture),
                        cancellationToken);

            Assert.Equal(
                PaymentSettlementStatus.Succeeded,
                captured.Status);
        }

        var refundRequestId =
            Guid.NewGuid();

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var decision =
                await new ApplyPaymentRefundRequestHandler(
                    new EfPaymentRefundRequestStore(
                        dbContext))
                    .HandleAsync(
                        new ApplyPaymentRefundRequestCommand(
                            refundRequestId,
                            paymentId,
                            orderId,
                            Guid.NewGuid(),
                            refundRequestId,
                            refundAmount,
                            "GBP",
                            requestedAtUtc.AddMinutes(1)),
                        cancellationToken);

            Assert.False(
                decision.Replayed);
        }

        return new RefundSetup(
            orderId,
            paymentId,
            refundRequestId);
    }

    private static async Task AssertRefundStateAsync(
        string connectionString,
        Guid requestId,
        PaymentRefundStatus expectedStatus,
        int expectedReconciliationAttempts,
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
                SELECT
                    status,
                    provider_invocation_claimed_at_utc IS NOT NULL,
                    provider_reference,
                    resolved_at_utc IS NOT NULL,
                    reconciliation_attempt_count
                FROM payments.refund_requests
                WHERE request_id = @request_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "request_id",
            requestId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(
            (int)expectedStatus,
            reader.GetInt32(0));

        Assert.True(
            reader.GetBoolean(1));

        if (expectedStatus ==
            PaymentRefundStatus.Succeeded)
        {
            Assert.False(
                reader.IsDBNull(2));
        }

        Assert.True(
            reader.GetBoolean(3));

        Assert.Equal(
            expectedReconciliationAttempts,
            reader.GetInt32(4));
    }

    private static async Task<T> ReadSingleOutcomeAsync<T>(
        string connectionString,
        Guid orderId,
        string messageType,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT payload::text
                FROM payments.outbox_messages
                WHERE correlation_id = @order_id
                  AND message_type = @message_type;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "message_type",
            messageType);

        var payloads =
            new List<string>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            payloads.Add(
                reader.GetString(0));
        }

        var payload =
            Assert.Single(
                payloads);

        return JsonSerializer.Deserialize<T>(
                   payload,
                   JsonSerializerOptions.Web) ??
               throw new InvalidOperationException(
                   "Payment refund outcome payload could not be deserialised.");
    }

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
            PaymentRefundExecutionTests.CreateDbContext(
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

    private sealed record RefundSetup(
        Guid OrderId,
        Guid PaymentId,
        Guid RefundRequestId);
}
