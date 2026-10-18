using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentAuthorisationInvocationSafetyTests
{
    [Fact]
    public async Task CrashAfterProviderInvocationClaimRecoversAsIndeterminateWithoutProviderRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var requestedAtUtc =
            new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using (var setupContext = CreateDbContext(connectionString))
        {
            var store = new EfPaymentAuthorisationStore(setupContext);

            await store.BeginAsync(
                new PaymentAuthorisationRequest(
                    requestId,
                    orderId,
                    new PaymentAmount(425m, "GBP"),
                    requestedAtUtc),
                cancellationToken);

            var claim = await store.ClaimProviderInvocationAsync(
                requestId,
                requestedAtUtc.AddSeconds(1),
                cancellationToken);

            Assert.True(claim.Acquired);
        }

        var provider = new RecordingProvider();

        await using var recoveryContext = CreateDbContext(connectionString);
        var handler = new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(recoveryContext),
            provider,
            new FixedTimeProvider(requestedAtUtc.AddMinutes(3)));

        var result = await handler.HandleAsync(
            new AuthorisePaymentCommand(
                requestId,
                orderId,
                425m,
                "GBP"),
            cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Indeterminate,
            result.Status);
        Assert.True(result.Replayed);
        Assert.Equal(0, provider.InvocationCount);

        var row = await ReadAttemptAsync(
            connectionString,
            requestId,
            cancellationToken);

        Assert.Equal(
            (int)PaymentAuthorisationStatus.Indeterminate,
            row.Status);
        Assert.NotNull(row.ProviderInvocationClaimedAtUtc);
    }

    [Fact]
    public async Task ConcurrentRecoveryWhileProviderCallIsInFlightNeverInvokesProviderTwice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var now =
            new DateTimeOffset(2026, 10, 18, 10, 10, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider = new BlockingAuthorisationProvider();

        await using var firstContext = CreateDbContext(connectionString);
        await using var secondContext = CreateDbContext(connectionString);

        var first = new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(firstContext),
            provider,
            new FixedTimeProvider(now));

        var second = new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(secondContext),
            provider,
            new FixedTimeProvider(now.AddMinutes(3)));

        var command = new AuthorisePaymentCommand(
            requestId,
            orderId,
            730m,
            "GBP");

        var firstTask = first.HandleAsync(
            command,
            cancellationToken);

        await provider.WaitUntilEnteredAsync(cancellationToken);

        var recovered = await second.HandleAsync(
            command,
            cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Indeterminate,
            recovered.Status);
        Assert.Equal(1, provider.InvocationCount);

        provider.ReleaseAuthorised();

        var original = await firstTask;

        Assert.Equal(
            PaymentAuthorisationStatus.Indeterminate,
            original.Status);
        Assert.Equal(1, provider.InvocationCount);

        var row = await ReadAttemptAsync(
            connectionString,
            requestId,
            cancellationToken);

        Assert.Equal(
            (int)PaymentAuthorisationStatus.Indeterminate,
            row.Status);
        Assert.NotNull(row.ProviderInvocationClaimedAtUtc);
    }

    [Fact]
    public async Task ExpiredInboxLeaseWhileProviderCallIsInFlightProducesOneIndeterminateOutcomeAndOneProviderInvocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var now =
            new DateTimeOffset(2026, 10, 18, 10, 20, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now);
        var provider = new BlockingAuthorisationProvider();
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var message = CreateEnvelope(
            requestId,
            orderId,
            999m,
            "GBP",
            now.AddMinutes(10),
            now);

        var consumer = new PaymentsInboundMessageConsumer(
            new TestPaymentsDbContextFactory(connectionString),
            provider,
            timeProvider);

        var firstTask = consumer.ConsumeAsync(
            message,
            cancellationToken);

        await provider.WaitUntilEnteredAsync(cancellationToken);

        // The original inbox processing lease is two minutes. A later broker
        // delivery may recover that inbox claim, but it must not re-authorise.
        timeProvider.Advance(TimeSpan.FromMinutes(3));

        await consumer.ConsumeAsync(
            message,
            cancellationToken);

        Assert.Equal(1, provider.InvocationCount);

        provider.ReleaseAuthorised();
        await firstTask;

        Assert.Equal(1, provider.InvocationCount);

        var row = await ReadAttemptAsync(
            connectionString,
            requestId,
            cancellationToken);

        Assert.Equal(
            (int)PaymentAuthorisationStatus.Indeterminate,
            row.Status);

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "inbox_messages",
                cancellationToken));

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "outbox_messages",
                cancellationToken));

        var outcomeType = await ReadSingleOutboxTypeAsync(
            connectionString,
            cancellationToken);

        Assert.Equal(
            PaymentAuthorisationIndeterminateV1.MessageType,
            outcomeType);
    }

    [Fact]
    public async Task CrashBeforeProviderInvocationClaimCanStillAuthoriseOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var requestedAtUtc =
            new DateTimeOffset(2026, 10, 18, 10, 30, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using (var setupContext = CreateDbContext(connectionString))
        {
            await new EfPaymentAuthorisationStore(setupContext)
                .BeginAsync(
                    new PaymentAuthorisationRequest(
                        requestId,
                        orderId,
                        new PaymentAmount(150m, "GBP"),
                        requestedAtUtc),
                    cancellationToken);
        }

        var provider = new RecordingProvider();

        await using var recoveryContext = CreateDbContext(connectionString);
        var handler = new AuthorisePaymentHandler(
            new EfPaymentAuthorisationStore(recoveryContext),
            provider,
            new FixedTimeProvider(requestedAtUtc.AddSeconds(5)));

        var result = await handler.HandleAsync(
            new AuthorisePaymentCommand(
                requestId,
                orderId,
                150m,
                "GBP"),
            cancellationToken);

        Assert.Equal(
            PaymentAuthorisationStatus.Authorised,
            result.Status);
        Assert.Equal(1, provider.InvocationCount);

        var row = await ReadAttemptAsync(
            connectionString,
            requestId,
            cancellationToken);

        Assert.Equal(
            (int)PaymentAuthorisationStatus.Authorised,
            row.Status);
        Assert.NotNull(row.ProviderInvocationClaimedAtUtc);
    }

    private static IntegrationMessageEnvelope CreateEnvelope(
        Guid requestId,
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset reservationValidUntilUtc,
        DateTimeOffset occurredAtUtc)
    {
        var command = new AuthorisePaymentV1(
            requestId,
            orderId,
            amount,
            currency,
            reservationValidUntilUtc);

        return new IntegrationMessageEnvelope(
            requestId,
            AuthorisePaymentV1.MessageType,
            JsonSerializer.Serialize(
                command,
                JsonSerializerOptions.Web),
            occurredAtUtc,
            orderId,
            Guid.NewGuid());
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

    private static async Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_payment_invocation_safety_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
    }

    private static async Task<AttemptRow> ReadAttemptAsync(
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
                provider_invocation_claimed_at_utc
            FROM payments.authorisation_attempts
            WHERE request_id = @request_id;
            """,
            connection);

        command.Parameters.AddWithValue("request_id", requestId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        return new AttemptRow(
            reader.GetInt32(0),
            reader.IsDBNull(1)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private static async Task<int> CountRowsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        if (tableName is not (
            "inbox_messages" or
            "outbox_messages"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM payments.{tableName};",
            connection);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(
            value,
            CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadSingleOutboxTypeAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT message_type
            FROM payments.outbox_messages;
            """,
            connection);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Assert.IsType<string>(value);
    }

    private sealed record AttemptRow(
        int Status,
        DateTimeOffset? ProviderInvocationClaimedAtUtc);

    private sealed class RecordingProvider : IPaymentAuthorisationProvider
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public Task<PaymentProviderAuthorisationResult> AuthoriseAsync(
            PaymentProviderAuthorisationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _invocationCount);

            return Task.FromResult(
                new PaymentProviderAuthorisationResult(
                    PaymentProviderAuthorisationOutcome.Authorised,
                    $"provider-{request.PaymentId:N}"));
        }
    }

    private sealed class BlockingAuthorisationProvider : IPaymentAuthorisationProvider
    {
        private readonly TaskCompletionSource<bool> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _invocationCount;
        private PaymentProviderAuthorisationRequest? _request;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public async Task<PaymentProviderAuthorisationResult> AuthoriseAsync(
            PaymentProviderAuthorisationRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _invocationCount) != 1)
            {
                throw new InvalidOperationException(
                    "Provider authorisation was invoked more than once.");
            }

            _request = request;
            _entered.TrySetResult(true);

            await _release.Task.WaitAsync(cancellationToken);

            return new PaymentProviderAuthorisationResult(
                PaymentProviderAuthorisationOutcome.Authorised,
                $"provider-{request.PaymentId:N}");
        }

        public Task<bool> WaitUntilEnteredAsync(
            CancellationToken cancellationToken) =>
            _entered.Task.WaitAsync(cancellationToken);

        public void ReleaseAuthorised()
        {
            if (_request is null)
            {
                throw new InvalidOperationException(
                    "Provider has not received an authorisation request.");
            }

            _release.TrySetResult(true);
        }
    }

    private sealed class TestPaymentsDbContextFactory :
        IDbContextFactory<PaymentsDbContext>
    {
        private readonly string _connectionString;

        public TestPaymentsDbContextFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public PaymentsDbContext CreateDbContext() =>
            PaymentAuthorisationInvocationSafetyTests.CreateDbContext(
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
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                duration,
                TimeSpan.Zero);

            _utcNow = _utcNow.Add(duration);
        }
    }
}