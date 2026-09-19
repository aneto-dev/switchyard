using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Application.SettlementReconciliation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Testcontainers.PostgreSql;
using Xunit;

namespace Switchyard.IntegrationTests;

public sealed class PaymentSettlementReconciliationTests
{
    [Fact]
    public async Task IndeterminateCaptureReconcilesToSucceededProviderTruth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 19, 0, 0, TimeSpan.Zero);
        var provider = CreateProvider(
            SimulatedPaymentSettlementReconciliationScenario.Succeed);
        var settlement = await CreateIndeterminateSettlementAsync(
            postgres.GetConnectionString(),
            provider,
            PaymentSettlementAction.Capture,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateReconciliationHandler(
            dbContext,
            provider,
            now.AddMinutes(2));

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(settlement.RequestId),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, result.Status);
        Assert.NotNull(result.ProviderReference);
        Assert.Equal(1, result.ReconciliationAttemptCount);
        Assert.Equal(1, provider.SettlementReconciliationInvocationCount);

        var row = await ReadReconciliationAsync(
            postgres.GetConnectionString(),
            settlement.RequestId,
            cancellationToken);

        Assert.Equal((int)PaymentSettlementStatus.Succeeded, row.Status);
        Assert.Equal(1, row.ReconciliationAttemptCount);
        Assert.Equal(now.AddMinutes(2), row.LastReconciledAtUtc);
        Assert.Equal(now.AddMinutes(2), row.ResolvedAtUtc);
        Assert.NotNull(row.ProviderReference);
    }

    [Fact]
    public async Task IndeterminateVoidCanReconcileToNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 19, 5, 0, TimeSpan.Zero);
        var provider = CreateProvider(
            SimulatedPaymentSettlementReconciliationScenario.NotApplied);
        var settlement = await CreateIndeterminateSettlementAsync(
            postgres.GetConnectionString(),
            provider,
            PaymentSettlementAction.Void,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateReconciliationHandler(
            dbContext,
            provider,
            now.AddMinutes(2));

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(settlement.RequestId),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.NotApplied, result.Status);
        Assert.Equal(1, result.ReconciliationAttemptCount);
        Assert.Equal(1, provider.SettlementReconciliationInvocationCount);
    }

    [Fact]
    public async Task UnknownSettlementTruthRemainsIndeterminateAndRecordsEachCheck()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 19, 10, 0, TimeSpan.Zero);
        var provider = CreateProvider(
            SimulatedPaymentSettlementReconciliationScenario.StillIndeterminate);
        var settlement = await CreateIndeterminateSettlementAsync(
            postgres.GetConnectionString(),
            provider,
            PaymentSettlementAction.Capture,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());

        var first = await CreateReconciliationHandler(
            dbContext,
            provider,
            now.AddMinutes(2))
            .HandleAsync(
                new ReconcilePaymentSettlementCommand(settlement.RequestId),
                cancellationToken);

        var second = await CreateReconciliationHandler(
            dbContext,
            provider,
            now.AddMinutes(3))
            .HandleAsync(
                new ReconcilePaymentSettlementCommand(settlement.RequestId),
                cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Indeterminate, first.Status);
        Assert.Equal(1, first.ReconciliationAttemptCount);
        Assert.Equal(PaymentSettlementStatus.Indeterminate, second.Status);
        Assert.Equal(2, second.ReconciliationAttemptCount);
        Assert.Equal(now.AddMinutes(3), second.LastReconciledAtUtc);
        Assert.Equal(2, provider.SettlementReconciliationInvocationCount);
    }

    [Fact]
    public async Task DefiniteSettlementDoesNotQueryProviderAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 19, 15, 0, TimeSpan.Zero);
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);
        var settlement = await CreateSuccessfulSettlementAsync(
            postgres.GetConnectionString(),
            provider,
            PaymentSettlementAction.Capture,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateReconciliationHandler(
            dbContext,
            provider,
            now.AddMinutes(2));

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(settlement.RequestId),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, result.Status);
        Assert.True(result.Replayed);
        Assert.Equal(0, result.ReconciliationAttemptCount);
        Assert.Equal(0, provider.SettlementReconciliationInvocationCount);
    }

    [Fact]
    public async Task ConcurrentReconciliationConvergesWithoutOverwritingProviderTruth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 19, 20, 0, TimeSpan.Zero);
        var seedProvider = CreateProvider(
            SimulatedPaymentSettlementReconciliationScenario.StillIndeterminate);
        var settlement = await CreateIndeterminateSettlementAsync(
            postgres.GetConnectionString(),
            seedProvider,
            PaymentSettlementAction.Capture,
            now,
            cancellationToken);

        await using var firstContext = CreateDbContext(postgres.GetConnectionString());
        await using var secondContext = CreateDbContext(postgres.GetConnectionString());

        var coordinatedProvider = new ConflictingCoordinatedReconciliationProvider();
        var firstHandler = new ReconcilePaymentSettlementHandler(
            new EfPaymentSettlementReconciliationStore(firstContext),
            coordinatedProvider,
            new FixedTimeProvider(now.AddMinutes(2)));
        var secondHandler = new ReconcilePaymentSettlementHandler(
            new EfPaymentSettlementReconciliationStore(secondContext),
            coordinatedProvider,
            new FixedTimeProvider(now.AddMinutes(2)));

        var command = new ReconcilePaymentSettlementCommand(settlement.RequestId);

        var results = await Task.WhenAll(
            firstHandler.HandleAsync(command, cancellationToken),
            secondHandler.HandleAsync(command, cancellationToken));

        var applied = Assert.Single(results, result => !result.Replayed);
        var replayed = Assert.Single(results, result => result.Replayed);

        Assert.True(
            applied.Status is PaymentSettlementStatus.Succeeded or
                              PaymentSettlementStatus.NotApplied);
        Assert.Equal(applied.Status, replayed.Status);
        Assert.Equal(applied.ProviderReference, replayed.ProviderReference);
        Assert.Equal(2, coordinatedProvider.InvocationCount);

        var row = await ReadReconciliationAsync(
            postgres.GetConnectionString(),
            settlement.RequestId,
            cancellationToken);

        Assert.Equal((int)applied.Status, row.Status);
        Assert.Equal(2, row.ReconciliationAttemptCount);

        if (applied.Status == PaymentSettlementStatus.Succeeded)
        {
            Assert.Equal(
                "provider-concurrent-settlement",
                row.ProviderReference);
        }
        else
        {
            Assert.Null(row.ProviderReference);
        }
    }

    private static SimulatedPaymentAuthorisationProvider CreateProvider(
        SimulatedPaymentSettlementReconciliationScenario reconciliationScenario)
    {
        return new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise,
            SimulatedPaymentReconciliationScenario.Authorise,
            SimulatedPaymentSettlementScenario.Indeterminate,
            reconciliationScenario);
    }

    private static async Task<ExecutePaymentSettlementResult> CreateIndeterminateSettlementAsync(
        string connectionString,
        SimulatedPaymentAuthorisationProvider provider,
        PaymentSettlementAction action,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var authorisation = await CreateAuthorisedPaymentAsync(
            connectionString,
            provider,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var handler = CreateSettlementHandler(
            dbContext,
            provider,
            now.AddMinutes(1));

        var result = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                Guid.NewGuid(),
                authorisation.PaymentId,
                action),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Indeterminate, result.Status);

        return result;
    }

    private static async Task<ExecutePaymentSettlementResult> CreateSuccessfulSettlementAsync(
        string connectionString,
        SimulatedPaymentAuthorisationProvider provider,
        PaymentSettlementAction action,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var authorisation = await CreateAuthorisedPaymentAsync(
            connectionString,
            provider,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var handler = CreateSettlementHandler(
            dbContext,
            provider,
            now.AddMinutes(1));

        var result = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                Guid.NewGuid(),
                authorisation.PaymentId,
                action),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, result.Status);

        return result;
    }

    private static async Task<AuthorisePaymentResult> CreateAuthorisedPaymentAsync(
        string connectionString,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        var handler = new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(dbContext),
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new AuthorisePaymentCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                299m,
                "GBP"),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Authorised, result.Status);

        return result;
    }

    private static ExecutePaymentSettlementHandler CreateSettlementHandler(
        PaymentsDbContext dbContext,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset now)
    {
        return new ExecutePaymentSettlementHandler(
            new EfPaymentSettlementStore(dbContext),
            provider,
            new FixedTimeProvider(now));
    }

    private static ReconcilePaymentSettlementHandler CreateReconciliationHandler(
        PaymentsDbContext dbContext,
        IPaymentSettlementReconciliationProvider provider,
        DateTimeOffset now)
    {
        return new ReconcilePaymentSettlementHandler(
            new EfPaymentSettlementReconciliationStore(dbContext),
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
            .WithDatabase("switchyard_payments_settlement_reconciliation_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
    }

    private static async Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<ReconciliationRow> ReadReconciliationAsync(
        string connectionString,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                status,
                reconciliation_attempt_count,
                last_reconciled_at_utc,
                resolved_at_utc,
                provider_reference
            FROM payments.settlement_attempts
            WHERE request_id = @request_id;
            """,
            connection);

        command.Parameters.AddWithValue("request_id", requestId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return new ReconciliationRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private sealed record ReconciliationRow(
        int Status,
        int ReconciliationAttemptCount,
        DateTimeOffset? LastReconciledAtUtc,
        DateTimeOffset? ResolvedAtUtc,
        string? ProviderReference);

    private sealed class ConflictingCoordinatedReconciliationProvider :
        IPaymentSettlementReconciliationProvider
    {
        private readonly TaskCompletionSource _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivalCount;

        public int InvocationCount => Volatile.Read(ref _arrivalCount);

        public async Task<PaymentProviderSettlementReconciliationResult> ReconcileAsync(
            PaymentProviderSettlementReconciliationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var arrival = Interlocked.Increment(ref _arrivalCount);

            if (arrival == 2)
            {
                _bothArrived.TrySetResult();
            }

            await _bothArrived.Task.WaitAsync(cancellationToken);

            return arrival == 1
                ? new PaymentProviderSettlementReconciliationResult(
                    PaymentProviderSettlementReconciliationOutcome.Succeeded,
                    "provider-concurrent-settlement")
                : new PaymentProviderSettlementReconciliationResult(
                    PaymentProviderSettlementReconciliationOutcome.NotApplied,
                    null);
        }
    }

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
