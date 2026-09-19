using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentAuthorisationTests
{
    [Fact]
    public async Task AuthorisedRequestPersistsAndReplaysWithoutSecondProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 17, 30, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateHandler(dbContext, provider, now);

        var first = await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 1299.99m, "GBP"),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 1299.99m, "GBP"),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Authorised, first.Status);
        Assert.False(first.Replayed);
        Assert.NotNull(first.ProviderReference);
        Assert.Equal(first.PaymentId, replay.PaymentId);
        Assert.Equal(first.ProviderReference, replay.ProviderReference);
        Assert.True(replay.Replayed);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, provider.UniqueAuthorisationCount);

        var row = await ReadPaymentAsync(
            postgres.GetConnectionString(),
            orderId,
            cancellationToken);

        Assert.Equal((int)PaymentAuthorisationStatus.Authorised, row.IntentStatus);
        Assert.Equal((int)PaymentAuthorisationStatus.Authorised, row.AttemptStatus);
        Assert.Equal(first.ProviderReference, row.ProviderReference);
        Assert.StartsWith("switchyard-auth-", row.ProviderIdempotencyKey, StringComparison.Ordinal);

        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "payment_intents",
                cancellationToken));
        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "authorisation_attempts",
                cancellationToken));
    }

    [Fact]
    public async Task IndeterminateRequestIsDurableAndReplayDoesNotBlindlyRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 17, 35, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Indeterminate);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateHandler(dbContext, provider, now);

        var first = await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 250m, "GBP"),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 250m, "GBP"),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, first.Status);
        Assert.Null(first.ProviderReference);
        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, provider.UniqueAuthorisationCount);

        var row = await ReadPaymentAsync(
            postgres.GetConnectionString(),
            orderId,
            cancellationToken);

        Assert.Equal((int)PaymentAuthorisationStatus.Indeterminate, row.IntentStatus);
        Assert.Equal((int)PaymentAuthorisationStatus.Indeterminate, row.AttemptStatus);
        Assert.Null(row.ProviderReference);
        Assert.False(string.IsNullOrWhiteSpace(row.ProviderIdempotencyKey));
    }

    [Fact]
    public async Task DeclinedRequestPersistsAndReplaysWithoutSecondProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 17, 37, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Decline);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateHandler(dbContext, provider, now);

        var first = await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 175m, "GBP"),
            cancellationToken);
        var replay = await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 175m, "GBP"),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Declined, first.Status);
        Assert.NotNull(first.ProviderReference);
        Assert.Equal(first.PaymentId, replay.PaymentId);
        Assert.Equal(first.ProviderReference, replay.ProviderReference);
        Assert.True(replay.Replayed);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, provider.UniqueAuthorisationCount);

        var row = await ReadPaymentAsync(
            postgres.GetConnectionString(),
            orderId,
            cancellationToken);

        Assert.Equal((int)PaymentAuthorisationStatus.Declined, row.IntentStatus);
        Assert.Equal((int)PaymentAuthorisationStatus.Declined, row.AttemptStatus);
        Assert.Equal(first.ProviderReference, row.ProviderReference);
    }
    [Fact]
    public async Task ReusingPaymentIdentitiesWithDifferentLogicalWorkConflicts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 17, 40, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateHandler(dbContext, provider, now);

        await handler.HandleAsync(
            new AuthorisePaymentCommand(requestId, orderId, 100m, "GBP"),
            cancellationToken);

        await Assert.ThrowsAsync<PaymentAuthorisationConflictException>(
            () => handler.HandleAsync(
                new AuthorisePaymentCommand(requestId, orderId, 101m, "GBP"),
                cancellationToken));

        await Assert.ThrowsAsync<PaymentAuthorisationConflictException>(
            () => handler.HandleAsync(
                new AuthorisePaymentCommand(Guid.NewGuid(), orderId, 100m, "GBP"),
                cancellationToken));

        Assert.Equal(1, provider.UniqueAuthorisationCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateRequestProducesOneProviderSideEffectAndOnePayment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 18, 17, 45, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new SimulatedPaymentAuthorisationProvider(
            SimulatedPaymentAuthorisationScenario.Authorise);

        await using var firstContext = CreateDbContext(postgres.GetConnectionString());
        await using var secondContext = CreateDbContext(postgres.GetConnectionString());

        var firstHandler = CreateHandler(firstContext, provider, now);
        var secondHandler = CreateHandler(secondContext, provider, now);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var firstTask = Task.Run(
            async () =>
            {
                ready.Signal();
                start.Wait(cancellationToken);

                return await firstHandler.HandleAsync(
                    new AuthorisePaymentCommand(requestId, orderId, 499m, "GBP"),
                    cancellationToken);
            },
            cancellationToken);

        var secondTask = Task.Run(
            async () =>
            {
                ready.Signal();
                start.Wait(cancellationToken);

                return await secondHandler.HandleAsync(
                    new AuthorisePaymentCommand(requestId, orderId, 499m, "GBP"),
                    cancellationToken);
            },
            cancellationToken);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        start.Set();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(
            results,
            result => Assert.Equal(
                PaymentAuthorisationStatus.Authorised,
                result.Status));
        Assert.Equal(results[0].PaymentId, results[1].PaymentId);
        Assert.Equal(results[0].ProviderReference, results[1].ProviderReference);
        Assert.Equal(1, provider.UniqueAuthorisationCount);

        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "payment_intents",
                cancellationToken));
        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "authorisation_attempts",
                cancellationToken));
    }

    private static AuthorisePaymentHandler CreateHandler(
        PaymentsDbContext dbContext,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset now)
    {
        return new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(dbContext),
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

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_payments_test")
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

    private static async Task<PaymentRow> ReadPaymentAsync(
        string connectionString, Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                intent.authorisation_status,
                attempt.status,
                attempt.provider_idempotency_key,
                attempt.provider_reference
            FROM payments.payment_intents AS intent
            INNER JOIN payments.authorisation_attempts AS attempt
                ON attempt.payment_id = intent.payment_id
            WHERE intent.order_id = @order_id;
            """,
            connection);

        command.Parameters.AddWithValue("order_id", orderId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return new PaymentRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<int> CountRowsAsync(
        string connectionString, string tableName,
        CancellationToken cancellationToken)
    {
        if (tableName is not ("payment_intents" or "authorisation_attempts"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM payments.{tableName};",
            connection);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private sealed record PaymentRow(
        int IntentStatus,
        int AttemptStatus,
        string ProviderIdempotencyKey,
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
