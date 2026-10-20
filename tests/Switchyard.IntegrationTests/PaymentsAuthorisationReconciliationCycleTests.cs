using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Reconciliation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentsAuthorisationReconciliationCycleTests
{
    [Fact]
    public async Task AuthorisedProviderTruthStagesAuthorisedOutcomeAtomically()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var initialAtUtc =
            new DateTimeOffset(
                2026,
                10,
                18,
                12,
                0,
                0,
                TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Indeterminate,
                SimulatedPaymentReconciliationScenario.Authorise);

        var payment =
            await CreateIndeterminatePaymentAsync(
                connectionString,
                provider,
                initialAtUtc,
                cancellationToken);

        var cycle =
            CreateCycle(
                connectionString,
                provider,
                initialAtUtc.AddSeconds(30),
                maxAttempts: 3);

        var result =
            await cycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(
            1,
            result.Due);
        Assert.Equal(
            1,
            result.Applied);
        Assert.Equal(
            1,
            result.Resolved);
        Assert.Equal(
            0,
            result.Escalated);
        Assert.Equal(
            0,
            result.Failed);

        var state =
            await ReadStateAsync(
                connectionString,
                payment.RequestId,
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Authorised,
            state.Status);
        Assert.Equal(
            1,
            state.ReconciliationAttemptCount);
        Assert.NotNull(
            state.ProviderReference);

        var outbox =
            Assert.Single(
                await ReadOutboxAsync(
                    connectionString,
                    cancellationToken));

        Assert.Equal(
            PaymentAuthorisedV1.MessageType,
            outbox.MessageType);
        Assert.Equal(
            payment.OrderId,
            outbox.CorrelationId);
        Assert.Equal(
            payment.RequestId,
            outbox.CausationId);

        var outcome =
            JsonSerializer.Deserialize<PaymentAuthorisedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(
            outcome);
        Assert.Equal(
            payment.RequestId,
            outcome.RequestId);
        Assert.Equal(
            payment.PaymentId,
            outcome.PaymentId);
        Assert.Equal(
            payment.OrderId,
            outcome.OrderId);
    }

    [Fact]
    public async Task DeclinedProviderTruthStagesFailedOutcomeAtomically()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var initialAtUtc =
            new DateTimeOffset(
                2026,
                10,
                18,
                12,
                10,
                0,
                TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Indeterminate,
                SimulatedPaymentReconciliationScenario.Decline);

        var payment =
            await CreateIndeterminatePaymentAsync(
                connectionString,
                provider,
                initialAtUtc,
                cancellationToken);

        var cycle =
            CreateCycle(
                connectionString,
                provider,
                initialAtUtc.AddSeconds(30),
                maxAttempts: 3);

        var result =
            await cycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(
            1,
            result.Applied);
        Assert.Equal(
            1,
            result.Resolved);

        var state =
            await ReadStateAsync(
                connectionString,
                payment.RequestId,
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Declined,
            state.Status);

        var outbox =
            Assert.Single(
                await ReadOutboxAsync(
                    connectionString,
                    cancellationToken));

        Assert.Equal(
            PaymentAuthorisationFailedV1.MessageType,
            outbox.MessageType);

        var outcome =
            JsonSerializer.Deserialize<PaymentAuthorisationFailedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(
            outcome);
        Assert.Equal(
            PaymentAuthorisationFailedV1.DeclinedReason,
            outcome.Reason);
        Assert.Equal(
            payment.RequestId,
            outcome.RequestId);
        Assert.Equal(
            payment.PaymentId,
            outcome.PaymentId);
        Assert.NotNull(
            outcome.ProviderReference);
    }


    [Fact]
    public async Task ReconciliationAndOutcomeStagingRollbackTogetherWhenOutboxStagingFails()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var initialAtUtc =
            new DateTimeOffset(
                2026,
                10,
                18,
                12,
                15,
                0,
                TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Indeterminate,
                SimulatedPaymentReconciliationScenario.Authorise);

        var payment =
            await CreateIndeterminatePaymentAsync(
                connectionString,
                provider,
                initialAtUtc,
                cancellationToken);

        await using (var dbContext =
                     CreateDbContext(
                         connectionString))
        {
            var store =
                new EfPaymentAuthorisationReconciliationStore(
                    dbContext);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    store.ApplyAtomicallyAsync(
                        new PaymentAuthorisationReconciliation(
                            payment.RequestId,
                            PaymentAuthorisationStatus.Authorised,
                            "provider-auth-rollback",
                            ExpectedAttemptCount: 0,
                            ReconciledAtUtc:
                                initialAtUtc.AddSeconds(10)),
                        (_, _) =>
                            Task.FromException(
                                new InvalidOperationException(
                                    "Deterministic outbox staging failure.")),
                        cancellationToken));
        }

        var state =
            await ReadStateAsync(
                connectionString,
                payment.RequestId,
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Indeterminate,
            state.Status);
        Assert.Equal(
            0,
            state.ReconciliationAttemptCount);
        Assert.Empty(
            await ReadOutboxAsync(
                connectionString,
                cancellationToken));
    }

    [Fact]
    public async Task UnknownProviderTruthEscalatesOnlyAtConfiguredAttemptLimit()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var initialAtUtc =
            new DateTimeOffset(
                2026,
                10,
                18,
                12,
                20,
                0,
                TimeSpan.Zero);

        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Indeterminate,
                SimulatedPaymentReconciliationScenario.StillIndeterminate);

        var payment =
            await CreateIndeterminatePaymentAsync(
                connectionString,
                provider,
                initialAtUtc,
                cancellationToken);

        var firstCycle =
            CreateCycle(
                connectionString,
                provider,
                initialAtUtc.AddSeconds(10),
                maxAttempts: 2);

        var first =
            await firstCycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(
            1,
            first.Applied);
        Assert.Equal(
            0,
            first.Escalated);
        Assert.Empty(
            await ReadOutboxAsync(
                connectionString,
                cancellationToken));

        var secondCycle =
            CreateCycle(
                connectionString,
                provider,
                initialAtUtc.AddSeconds(20),
                maxAttempts: 2);

        var second =
            await secondCycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(
            1,
            second.Applied);
        Assert.Equal(
            1,
            second.Escalated);

        var state =
            await ReadStateAsync(
                connectionString,
                payment.RequestId,
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Indeterminate,
            state.Status);
        Assert.Equal(
            2,
            state.ReconciliationAttemptCount);

        var outbox =
            Assert.Single(
                await ReadOutboxAsync(
                    connectionString,
                    cancellationToken));

        Assert.Equal(
            PaymentAuthorisationNeedsAttentionV1.MessageType,
            outbox.MessageType);

        var outcome =
            JsonSerializer.Deserialize<PaymentAuthorisationNeedsAttentionV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(
            outcome);
        Assert.Equal(
            payment.RequestId,
            outcome.RequestId);
        Assert.Equal(
            2,
            outcome.ReconciliationAttemptCount);
        Assert.Equal(
            PaymentAuthorisationNeedsAttentionV1.ReconciliationExhaustedReason,
            outcome.Reason);

        var thirdCycle =
            CreateCycle(
                connectionString,
                provider,
                initialAtUtc.AddSeconds(30),
                maxAttempts: 2);

        var third =
            await thirdCycle.ReconcileAsync(
                cancellationToken);

        Assert.Equal(
            0,
            third.Due);
        Assert.Single(
            await ReadOutboxAsync(
                connectionString,
                cancellationToken));
    }

    [Fact]
    public async Task ConcurrentReconciliationUsesOptimisticAttemptIdentityAndStagesOneOutcome()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var initialAtUtc =
            new DateTimeOffset(
                2026,
                10,
                18,
                12,
                30,
                0,
                TimeSpan.Zero);

        var authorisationProvider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Indeterminate,
                SimulatedPaymentReconciliationScenario.StillIndeterminate);

        var payment =
            await CreateIndeterminatePaymentAsync(
                connectionString,
                authorisationProvider,
                initialAtUtc,
                cancellationToken);

        var reconciliationProvider =
            new CoordinatedReconciliationProvider(
                expectedCalls: 2);

        var firstCycle =
            CreateCycle(
                connectionString,
                reconciliationProvider,
                initialAtUtc.AddSeconds(10),
                maxAttempts: 3);

        var secondCycle =
            CreateCycle(
                connectionString,
                reconciliationProvider,
                initialAtUtc.AddSeconds(10),
                maxAttempts: 3);

        var results =
            await Task.WhenAll(
                firstCycle.ReconcileAsync(
                    cancellationToken),
                secondCycle.ReconcileAsync(
                    cancellationToken));

        Assert.Equal(
            2,
            reconciliationProvider.InvocationCount);
        Assert.Equal(
            1,
            results.Sum(
                result =>
                    result.Applied));
        Assert.Equal(
            1,
            results.Sum(
                result =>
                    result.Resolved));

        var state =
            await ReadStateAsync(
                connectionString,
                payment.RequestId,
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Authorised,
            state.Status);
        Assert.Equal(
            1,
            state.ReconciliationAttemptCount);

        Assert.Single(
            await ReadOutboxAsync(
                connectionString,
                cancellationToken));
    }

    private static PaymentsAuthorisationReconciliationCycle CreateCycle(
        string connectionString,
        IPaymentAuthorisationReconciliationProvider provider,
        DateTimeOffset nowUtc,
        int maxAttempts)
    {
        return new PaymentsAuthorisationReconciliationCycle(
            new TestPaymentsDbContextFactory(
                connectionString),
            provider,
            new FixedTimeProvider(
                nowUtc),
            new PaymentsAuthorisationReconciliationOptions(
                batchSize: 10,
                maxAttempts: maxAttempts,
                retryDelay: TimeSpan.Zero,
                pollInterval: TimeSpan.FromSeconds(1)),
            NullLogger<PaymentsAuthorisationReconciliationCycle>.Instance);
    }

    private static async Task<SeededPayment> CreateIndeterminatePaymentAsync(
        string connectionString,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        var requestId =
            Guid.NewGuid();
        var orderId =
            Guid.NewGuid();

        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new AuthorisePaymentHandler(
                new EfPaymentAuthorisationStore(
                    dbContext),
                provider,
                new FixedTimeProvider(
                    requestedAtUtc));

        var result =
            await handler.HandleAsync(
                new AuthorisePaymentCommand(
                    requestId,
                    orderId,
                    275m,
                    "GBP"),
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Indeterminate,
            result.Status);

        return new SeededPayment(
            requestId,
            orderId,
            result.PaymentId);
    }

    private static async Task<PaymentAuthorisationReconciliationState> ReadStateAsync(
        string connectionString,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var state =
            await new EfPaymentAuthorisationReconciliationStore(
                dbContext)
                .GetAsync(
                    requestId,
                    cancellationToken);

        return Assert.IsType<PaymentAuthorisationReconciliationState>(
            state);
    }

    private static async Task<IReadOnlyList<OutboxRow>> ReadOutboxAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var rows =
            new List<OutboxRow>();

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
                    payload::text,
                    correlation_id,
                    causation_id
                FROM payments.outbox_messages
                ORDER BY occurred_at_utc, message_id;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            rows.Add(
                new OutboxRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetGuid(3)));
        }

        return rows;
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

    private static async Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres =
            new PostgreSqlBuilder(
                "postgres:18-alpine")
                .WithDatabase(
                    "switchyard_payments_reconciliation_cycle_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        return postgres;
    }

    private sealed record SeededPayment(
        Guid RequestId,
        Guid OrderId,
        Guid PaymentId);

    private sealed record OutboxRow(
        string MessageType,
        string PayloadJson,
        Guid CorrelationId,
        Guid? CausationId);

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
            PaymentsAuthorisationReconciliationCycleTests.CreateDbContext(
                _connectionString);

        public Task<PaymentsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class CoordinatedReconciliationProvider :
        IPaymentAuthorisationReconciliationProvider
    {
        private readonly int _expectedCalls;
        private readonly TaskCompletionSource<bool> _ready =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private int _invocationCount;

        public CoordinatedReconciliationProvider(
            int expectedCalls)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                expectedCalls);

            _expectedCalls =
                expectedCalls;
        }

        public int InvocationCount =>
            Volatile.Read(
                ref _invocationCount);

        public async Task<PaymentProviderReconciliationResult> ReconcileAsync(
            PaymentProviderReconciliationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                request);

            Interlocked.Increment(
                ref _invocationCount);

            if (Interlocked.Increment(
                    ref _arrived) ==
                _expectedCalls)
            {
                _ready.TrySetResult(
                    true);
            }

            await _ready.Task.WaitAsync(
                cancellationToken);

            return new PaymentProviderReconciliationResult(
                PaymentProviderReconciliationOutcome.Authorised,
                "provider-auth-concurrent");
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
