using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Reconciliation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Testcontainers.PostgreSql;
using Xunit;

namespace Switchyard.IntegrationTests;

public sealed class PaymentAuthorisationReconciliationTests
{
    [Fact]
    public async Task IndeterminateAuthorisationReconcilesToAuthorisedProviderTruth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var authorisedAt = new DateTimeOffset(2026, 9, 19, 14, 30, 0, TimeSpan.Zero);
        var reconciledAt = authorisedAt.AddMinutes(2);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Indeterminate,
            SimulatedPaymentReconciliationScenario.Authorise);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var authoriseHandler = CreateAuthoriseHandler(dbContext, provider, authorisedAt);

        var initial = await authoriseHandler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 249m, "GBP"),
            cancellationToken);

        var reconciliationHandler = CreateReconciliationHandler(
            dbContext,
            provider,
            reconciledAt);

        var reconciled = await reconciliationHandler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(requestId),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, initial.Status);
        Assert.Equal(PaymentAuthorisationStatus.Authorised, reconciled.Status);
        Assert.NotNull(reconciled.ProviderReference);
        Assert.Equal(1, reconciled.ReconciliationAttemptCount);
        Assert.Equal(reconciledAt, reconciled.LastReconciledAtUtc);
        Assert.False(reconciled.Replayed);
        Assert.Equal(1, provider.ReconciliationInvocationCount);

        var row = await ReadReconciliationAsync(
            postgres.GetConnectionString(),
            requestId,
            cancellationToken);

        Assert.Equal((int)PaymentAuthorisationStatus.Authorised, row.AttemptStatus);
        Assert.Equal((int)PaymentAuthorisationStatus.Authorised, row.IntentStatus);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(reconciledAt, row.LastReconciledAtUtc);
        Assert.Equal(reconciledAt, row.ResolvedAtUtc);
        Assert.NotNull(row.ProviderReference);
    }

    [Fact]
    public async Task IndeterminateAuthorisationReconcilesToDeclinedProviderTruth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var authorisedAt = new DateTimeOffset(2026, 9, 19, 14, 35, 0, TimeSpan.Zero);
        var reconciledAt = authorisedAt.AddMinutes(2);
        var requestId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Indeterminate,
            SimulatedPaymentReconciliationScenario.Decline);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var authoriseHandler = CreateAuthoriseHandler(dbContext, provider, authorisedAt);

        await authoriseHandler.HandleAsync(
            new AuthorisePaymentCommand(requestId, Guid.NewGuid(), 199m, "GBP"),
            cancellationToken);

        var reconciliationHandler = CreateReconciliationHandler(
            dbContext,
            provider,
            reconciledAt);

        var reconciled = await reconciliationHandler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(requestId),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Declined, reconciled.Status);
        Assert.NotNull(reconciled.ProviderReference);
        Assert.Equal(1, reconciled.ReconciliationAttemptCount);
        Assert.Equal(1, provider.ReconciliationInvocationCount);
    }

    [Fact]
    public async Task UnknownProviderTruthRemainsIndeterminateAndRecordsEachCheck()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var authorisedAt = new DateTimeOffset(2026, 9, 19, 14, 40, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Indeterminate,
            SimulatedPaymentReconciliationScenario.StillIndeterminate);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var authoriseHandler = CreateAuthoriseHandler(dbContext, provider, authorisedAt);

        await authoriseHandler.HandleAsync(
            new AuthorisePaymentCommand(requestId, Guid.NewGuid(), 349m, "GBP"),
            cancellationToken);

        var firstHandler = CreateReconciliationHandler(
            dbContext,
            provider,
            authorisedAt.AddMinutes(1));
        var first = await firstHandler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(requestId),
            cancellationToken);

        var secondHandler = CreateReconciliationHandler(
            dbContext,
            provider,
            authorisedAt.AddMinutes(2));
        var second = await secondHandler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(requestId),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, first.Status);
        Assert.Equal(1, first.ReconciliationAttemptCount);
        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, second.Status);
        Assert.Equal(2, second.ReconciliationAttemptCount);
        Assert.Equal(authorisedAt.AddMinutes(2), second.LastReconciledAtUtc);
        Assert.Equal(2, provider.ReconciliationInvocationCount);
    }

    [Fact]
    public async Task DefiniteAuthorisationDoesNotQueryProviderAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 14, 45, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var authoriseHandler = CreateAuthoriseHandler(dbContext, provider, now);

        await authoriseHandler.HandleAsync(
            new AuthorisePaymentCommand(requestId, Guid.NewGuid(), 499m, "GBP"),
            cancellationToken);

        var reconciliationHandler = CreateReconciliationHandler(
            dbContext,
            provider,
            now.AddMinutes(1));

        var result = await reconciliationHandler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(requestId),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Authorised, result.Status);
        Assert.True(result.Replayed);
        Assert.Equal(0, result.ReconciliationAttemptCount);
        Assert.Equal(0, provider.ReconciliationInvocationCount);
    }

    private static AuthorisePaymentHandler CreateAuthoriseHandler(
        PaymentsDbContext dbContext,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset now)
    {
        return new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(dbContext),
            provider,
            new FixedTimeProvider(now));
    }

    private static ReconcilePaymentAuthorisationHandler CreateReconciliationHandler(
        PaymentsDbContext dbContext,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset now)
    {
        return new ReconcilePaymentAuthorisationHandler(
            new EfPaymentAuthorisationReconciliationStore(dbContext),
            provider,
            new FixedTimeProvider(now));
    }

    private static PaymentsDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PaymentsDbContext(options);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_payments_reconciliation_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
    }

    private static async Task ApplyMigrationsAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<ReconciliationRow> ReadReconciliationAsync(
        string connectionString, Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                attempt.status,
                intent.authorisation_status,
                attempt.reconciliation_attempt_count,
                attempt.last_reconciled_at_utc,
                attempt.resolved_at_utc,
                attempt.provider_reference
            FROM payments.authorisation_attempts AS attempt
            INNER JOIN payments.payment_intents AS intent
                ON intent.payment_id = attempt.payment_id
            WHERE attempt.request_id = @request_id;
            """,
            connection);

        command.Parameters.AddWithValue("request_id", requestId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return new ReconciliationRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private sealed record ReconciliationRow(
        int AttemptStatus,
        int IntentStatus,
        int AttemptCount,
        DateTimeOffset? LastReconciledAtUtc,
        DateTimeOffset? ResolvedAtUtc,
        string? ProviderReference);

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
