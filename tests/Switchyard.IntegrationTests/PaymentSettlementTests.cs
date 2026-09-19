using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Testcontainers.PostgreSql;
using Xunit;

namespace Switchyard.IntegrationTests;

public sealed class PaymentSettlementTests
{
    [Fact]
    public async Task AuthorisedPaymentCanBeCapturedOnceAndReplayed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);
        var payment = await CreateAuthorisedPaymentAsync(
            postgres.GetConnectionString(),
            provider,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateSettlementHandler(dbContext, provider, now.AddMinutes(1));
        var requestId = Guid.NewGuid();

        var first = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                requestId,
                payment.PaymentId,
                PaymentSettlementAction.Capture),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                requestId,
                payment.PaymentId,
                PaymentSettlementAction.Capture),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, first.Status);
        Assert.NotNull(first.ProviderReference);
        Assert.Equal(first.ProviderReference, replay.ProviderReference);
        Assert.True(replay.Replayed);
        Assert.Equal(1, provider.SettlementInvocationCount);
        Assert.Equal(1, provider.UniqueSettlementCount);

        var row = await ReadSettlementAsync(
            postgres.GetConnectionString(),
            payment.PaymentId,
            cancellationToken);

        Assert.Equal((int)PaymentSettlementAction.Capture, row.Action);
        Assert.Equal((int)PaymentSettlementStatus.Succeeded, row.Status);
        Assert.Equal(first.ProviderReference, row.ProviderReference);
    }

    [Fact]
    public async Task AuthorisedPaymentCanBeVoidedOnceAndReplayed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 17, 5, 0, TimeSpan.Zero);
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);
        var payment = await CreateAuthorisedPaymentAsync(
            postgres.GetConnectionString(),
            provider,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateSettlementHandler(dbContext, provider, now.AddMinutes(1));
        var requestId = Guid.NewGuid();

        var first = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                requestId,
                payment.PaymentId,
                PaymentSettlementAction.Void),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                requestId,
                payment.PaymentId,
                PaymentSettlementAction.Void),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, first.Status);
        Assert.NotNull(first.ProviderReference);
        Assert.Equal(first.ProviderReference, replay.ProviderReference);
        Assert.True(replay.Replayed);
        Assert.Equal(1, provider.SettlementInvocationCount);
        Assert.Equal(1, provider.UniqueSettlementCount);
    }

    [Fact]
    public async Task DeclinedAuthorisationCannotBeCaptured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 17, 10, 0, TimeSpan.Zero);
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Decline);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var authoriseHandler = CreateAuthoriseHandler(dbContext, provider, now);
        var authorisation = await authoriseHandler.HandleAsync(
            new AuthorisePaymentCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                149m,
                "GBP"),
            cancellationToken);

        var settlementHandler = CreateSettlementHandler(
            dbContext,
            provider,
            now.AddMinutes(1));

        var exception = await Assert.ThrowsAsync<PaymentSettlementNotAllowedException>(
            () => settlementHandler.HandleAsync(
                new ExecutePaymentSettlementCommand(
                    Guid.NewGuid(),
                    authorisation.PaymentId,
                    PaymentSettlementAction.Capture),
                cancellationToken));

        Assert.Equal(
            PaymentAuthorisationStatus.Declined,
            exception.AuthorisationStatus);
        Assert.Equal(0, provider.SettlementInvocationCount);
    }

    [Fact]
    public async Task IndeterminateSettlementIsDurableAndDoesNotBlindlyRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 17, 15, 0, TimeSpan.Zero);
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise,
            SimulatedPaymentReconciliationScenario.Authorise,
            SimulatedPaymentSettlementScenario.Indeterminate);
        var payment = await CreateAuthorisedPaymentAsync(
            postgres.GetConnectionString(),
            provider,
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateSettlementHandler(dbContext, provider, now.AddMinutes(1));
        var requestId = Guid.NewGuid();

        var first = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                requestId,
                payment.PaymentId,
                PaymentSettlementAction.Capture),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                requestId,
                payment.PaymentId,
                PaymentSettlementAction.Capture),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Indeterminate, first.Status);
        Assert.Equal(PaymentSettlementStatus.Indeterminate, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(1, provider.SettlementInvocationCount);
        Assert.Equal(1, provider.UniqueSettlementCount);
    }

    [Fact]
    public async Task ConcurrentCaptureAndVoidAllowOnlyOneSettlementWorkflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 17, 20, 0, TimeSpan.Zero);
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);
        var payment = await CreateAuthorisedPaymentAsync(
            postgres.GetConnectionString(),
            provider,
            now,
            cancellationToken);

        await using var captureContext = CreateDbContext(postgres.GetConnectionString());
        await using var voidContext = CreateDbContext(postgres.GetConnectionString());

        var captureHandler = CreateSettlementHandler(
            captureContext,
            provider,
            now.AddMinutes(1));
        var voidHandler = CreateSettlementHandler(
            voidContext,
            provider,
            now.AddMinutes(1));

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var captureTask = Task.Run(
            () => ExecuteWithConflictCaptureAsync(
                captureHandler,
                new ExecutePaymentSettlementCommand(
                    Guid.NewGuid(),
                    payment.PaymentId,
                    PaymentSettlementAction.Capture),
                ready,
                start,
                cancellationToken),
            cancellationToken);

        var voidTask = Task.Run(
            () => ExecuteWithConflictCaptureAsync(
                voidHandler,
                new ExecutePaymentSettlementCommand(
                    Guid.NewGuid(),
                    payment.PaymentId,
                    PaymentSettlementAction.Void),
                ready,
                start,
                cancellationToken),
            cancellationToken);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        start.Set();

        var outcomes = await Task.WhenAll(captureTask, voidTask);

        Assert.Equal(1, outcomes.Count(outcome => outcome.Succeeded));
        Assert.Equal(1, outcomes.Count(outcome => outcome.Conflict));
        Assert.Equal(1, provider.UniqueSettlementCount);

        Assert.Equal(
            1,
            await CountSettlementsAsync(
                postgres.GetConnectionString(),
                payment.PaymentId,
                cancellationToken));
    }

    private static async Task<SettlementOutcome> ExecuteWithConflictCaptureAsync(
        ExecutePaymentSettlementHandler handler,
        ExecutePaymentSettlementCommand command,
        CountdownEvent ready,
        ManualResetEventSlim start,
        CancellationToken cancellationToken)
    {
        ready.Signal();
        start.Wait(cancellationToken);

        try
        {
            await handler.HandleAsync(command, cancellationToken);
            return new SettlementOutcome(Succeeded: true, Conflict: false);
        }
        catch (PaymentSettlementConflictException)
        {
            return new SettlementOutcome(Succeeded: false, Conflict: true);
        }
    }

    private static async Task<AuthorisePaymentResult> CreateAuthorisedPaymentAsync(
        string connectionString,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        var handler = CreateAuthoriseHandler(dbContext, provider, now);

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
            .WithDatabase("switchyard_payments_settlement_test")
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

    private static async Task<SettlementRow> ReadSettlementAsync(
        string connectionString,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT action, status, provider_reference
            FROM payments.settlement_attempts
            WHERE payment_id = @payment_id;
            """,
            connection);

        command.Parameters.AddWithValue("payment_id", paymentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return new SettlementRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<int> CountSettlementsAsync(
        string connectionString,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM payments.settlement_attempts
            WHERE payment_id = @payment_id;
            """,
            connection);

        command.Parameters.AddWithValue("payment_id", paymentId);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private sealed record SettlementRow(
        int Action,
        int Status,
        string? ProviderReference);

    private sealed record SettlementOutcome(
        bool Succeeded,
        bool Conflict);

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
