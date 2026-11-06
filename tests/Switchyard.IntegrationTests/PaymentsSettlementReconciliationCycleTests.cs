using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentsSettlementReconciliationCycleTests
{
    [Fact]
    public async Task SuccessfulCaptureReconciliationStagesFinalOutcomeOnce()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder(
                "postgres:18-alpine")
                .WithDatabase(
                    "switchyard_settlement_reconciliation_cycle_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await using (var migrations =
            CreateDbContext(
                connectionString))
        {
            await migrations.Database.MigrateAsync(
                cancellationToken);
        }

        var requestedAtUtc =
            new DateTimeOffset(
                2026,
                11,
                5,
                15,
                0,
                0,
                TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise,
                SimulatedPaymentReconciliationScenario.Authorise,
                SimulatedPaymentSettlementScenario.Indeterminate,
                SimulatedPaymentSettlementReconciliationScenario.Succeed);

        var orderId =
            Guid.NewGuid();

        Guid paymentId;

        await using (var dbContext =
            CreateDbContext(
                connectionString))
        {
            var authorised =
                await new AuthorisePaymentHandler(
                    new EfPaymentAuthorisationStore(
                        dbContext),
                    provider,
                    new FixedTimeProvider(
                        requestedAtUtc))
                    .HandleAsync(
                        new AuthorisePaymentCommand(
                            Guid.NewGuid(),
                            orderId,
                            499.99m,
                            "GBP"),
                        cancellationToken);

            paymentId =
                authorised.PaymentId;
        }

        var captureRequestId =
            Guid.NewGuid();

        await using (var dbContext =
            CreateDbContext(
                connectionString))
        {
            var capture =
                await new ExecutePaymentSettlementHandler(
                    new EfPaymentSettlementStore(
                        dbContext),
                    provider,
                    new FixedTimeProvider(
                        requestedAtUtc.AddMinutes(1)))
                    .HandleAsync(
                        new ExecutePaymentSettlementCommand(
                            captureRequestId,
                            paymentId,
                            PaymentSettlementAction.Capture),
                        cancellationToken);

            Assert.Equal(
                PaymentSettlementStatus.Indeterminate,
                capture.Status);
        }

        var cycle =
            new PaymentsSettlementReconciliationCycle(
                new TestPaymentsDbContextFactory(
                    connectionString),
                provider,
                new FixedTimeProvider(
                    requestedAtUtc.AddMinutes(2)),
                new PaymentsSettlementReconciliationOptions(
                    batchSize: 10,
                    maxAttempts: 3,
                    retryDelay: TimeSpan.Zero,
                    pollInterval: TimeSpan.FromSeconds(1)),
                NullLogger<PaymentsSettlementReconciliationCycle>.Instance);

        var first =
            await cycle.ReconcileAsync(
                cancellationToken);

        var second =
            await cycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(1, first.Due);
        Assert.Equal(1, first.Applied);
        Assert.Equal(1, first.Resolved);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, second.Due);

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    message_type,
                    payload::text
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
            PaymentCapturedV1.MessageType);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        var outcome =
            JsonSerializer.Deserialize<PaymentCapturedV1>(
                reader.GetString(1),
                JsonSerializerOptions.Web);

        Assert.NotNull(outcome);
        Assert.Equal(
            captureRequestId,
            outcome.RequestId);
        Assert.Equal(
            paymentId,
            outcome.PaymentId);
        Assert.Equal(
            orderId,
            outcome.OrderId);

        Assert.False(
            await reader.ReadAsync(
                cancellationToken));
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
            PaymentsSettlementReconciliationCycleTests.CreateDbContext(
                _connectionString);

        public Task<PaymentsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider :
        TimeProvider
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
