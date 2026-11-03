using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Payments;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentCaptureInboundMessageTests
{
    [Fact]
    public async Task CaptureCommandUsesExistingSettlementIdempotencyAcrossReplay()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_capture_inbound_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await using (var migrations = CreateDbContext(connectionString))
        {
            await migrations.Database.MigrateAsync(cancellationToken);
        }

        var now =
            new DateTimeOffset(
                2026,
                11,
                3,
                12,
                10,
                0,
                TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);

        var authorisationRequestId =
            Guid.NewGuid();

        var orderId =
            Guid.NewGuid();

        Guid paymentId;

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var result =
                await new AuthorisePaymentHandler(
                    new EfPaymentAuthorisationStore(dbContext),
                    provider,
                    new FixedTimeProvider(now)).HandleAsync(
                    new AuthorisePaymentCommand(
                        authorisationRequestId,
                        orderId,
                        1299.99m,
                        "GBP"),
                    cancellationToken);

            paymentId = result.PaymentId;
        }

        var captureRequestId =
            Guid.NewGuid();

        var command =
            new CapturePaymentV1(
                captureRequestId,
                orderId,
                paymentId);

        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                CapturePaymentV1.MessageType,
                JsonSerializer.Serialize(
                    command,
                    JsonSerializerOptions.Web),
                now.AddMinutes(1),
                orderId,
                Guid.NewGuid());

        var consumer =
            new PaymentsInboundMessageConsumer(
                new TestPaymentsDbContextFactory(
                    connectionString),
                provider,
                provider,
                new FixedTimeProvider(
                    now.AddMinutes(1)));

        await consumer.ConsumeAsync(
            envelope,
            cancellationToken);

        await consumer.ConsumeAsync(
            envelope,
            cancellationToken);

        Assert.Equal(
            1,
            provider.SettlementInvocationCount);

        await using var connection =
            new Npgsql.NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var sql =
            new Npgsql.NpgsqlCommand(
                """
                SELECT action, status
                FROM payments.settlement_attempts
                WHERE request_id = @request_id;
                """,
                connection);

        sql.Parameters.AddWithValue(
            "request_id",
            captureRequestId);

        await using var reader =
            await sql.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(
            (int)PaymentSettlementAction.Capture,
            reader.GetInt32(0));

        Assert.Equal(
            (int)PaymentSettlementStatus.Succeeded,
            reader.GetInt32(1));
    }

    private static PaymentsDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new PaymentsDbContext(options);
    }

    private sealed class TestPaymentsDbContextFactory :
        IDbContextFactory<PaymentsDbContext>
    {
        private readonly string _connectionString;

        public TestPaymentsDbContextFactory(
            string connectionString)
        {
            _connectionString = connectionString;
        }

        public PaymentsDbContext CreateDbContext() =>
            PaymentCaptureInboundMessageTests.CreateDbContext(
                _connectionString);

        public Task<PaymentsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
