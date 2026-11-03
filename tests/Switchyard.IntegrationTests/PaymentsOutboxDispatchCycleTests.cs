using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Messaging;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentsOutboxDispatchCycleTests
{
    [Fact]
    public async Task DispatchPublishesPaymentOutcomeAndMarksItPublishedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var occurredAtUtc =
            new DateTimeOffset(2026, 10, 18, 0, 10, 0, TimeSpan.Zero);
        var dispatchAtUtc = occurredAtUtc.AddSeconds(1);
        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                "payments.event.authorised.v1",
                """{"requestId":"00000000-0000-0000-0000-000000000001"}""",
                occurredAtUtc,
                Guid.NewGuid(),
                Guid.NewGuid());

        await AddOutboxMessageAsync(
            postgres.GetConnectionString(),
            envelope,
            cancellationToken);

        var transport = new RecordingTransport();
        var cycle = CreateCycle(
            postgres.GetConnectionString(),
            transport,
            dispatchAtUtc,
            retryDelay: TimeSpan.FromSeconds(30));

        var first = await cycle.DispatchAsync(cancellationToken);
        var second = await cycle.DispatchAsync(cancellationToken);

        Assert.Equal(1, first.Claimed);
        Assert.Equal(1, first.Published);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, second.Claimed);
        Assert.Single(transport.Published);
        AssertEnvelopeEquivalent(envelope, transport.Published[0]);

        var row = await ReadOutboxStateAsync(
            postgres.GetConnectionString(),
            envelope.MessageId,
            cancellationToken);

        Assert.Equal(1, row.DeliveryAttemptCount);
        Assert.Equal(dispatchAtUtc, row.LastAttemptAtUtc);
        Assert.Equal(dispatchAtUtc, row.PublishedAtUtc);
        Assert.Null(row.LastError);
        Assert.Null(row.LockToken);
        Assert.Null(row.LockedUntilUtc);
    }

    [Fact]
    public async Task TransportFailureSchedulesRetryWithoutLosingPaymentOutcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var occurredAtUtc =
            new DateTimeOffset(2026, 10, 18, 0, 20, 0, TimeSpan.Zero);
        var firstAttemptAtUtc = occurredAtUtc.AddSeconds(1);
        var retryDelay = TimeSpan.FromSeconds(30);
        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                "payments.event.authorisation-indeterminate.v1",
                """{"requestId":"00000000-0000-0000-0000-000000000002"}""",
                occurredAtUtc,
                Guid.NewGuid(),
                Guid.NewGuid());

        await AddOutboxMessageAsync(
            postgres.GetConnectionString(),
            envelope,
            cancellationToken);

        var failingTransport = new RecordingTransport(fail: true);
        var firstCycle = CreateCycle(
            postgres.GetConnectionString(),
            failingTransport,
            firstAttemptAtUtc,
            retryDelay);

        var failed = await firstCycle.DispatchAsync(cancellationToken);

        Assert.Equal(1, failed.Claimed);
        Assert.Equal(0, failed.Published);
        Assert.Equal(1, failed.Failed);

        var failedRow = await ReadOutboxStateAsync(
            postgres.GetConnectionString(),
            envelope.MessageId,
            cancellationToken);

        Assert.Equal(1, failedRow.DeliveryAttemptCount);
        Assert.Equal(firstAttemptAtUtc, failedRow.LastAttemptAtUtc);
        Assert.Null(failedRow.PublishedAtUtc);
        Assert.Equal(nameof(TimeoutException), failedRow.LastError);
        Assert.Equal(
            firstAttemptAtUtc.Add(retryDelay),
            failedRow.AvailableAtUtc);
        Assert.Null(failedRow.LockToken);
        Assert.Null(failedRow.LockedUntilUtc);

        var successfulTransport = new RecordingTransport();
        var retryAtUtc = firstAttemptAtUtc.Add(retryDelay);
        var retryCycle = CreateCycle(
            postgres.GetConnectionString(),
            successfulTransport,
            retryAtUtc,
            retryDelay);

        var retried = await retryCycle.DispatchAsync(cancellationToken);

        Assert.Equal(1, retried.Claimed);
        Assert.Equal(1, retried.Published);
        Assert.Equal(0, retried.Failed);
        Assert.Single(successfulTransport.Published);
        AssertEnvelopeEquivalent(envelope, successfulTransport.Published[0]);

        var publishedRow = await ReadOutboxStateAsync(
            postgres.GetConnectionString(),
            envelope.MessageId,
            cancellationToken);

        Assert.Equal(2, publishedRow.DeliveryAttemptCount);
        Assert.Equal(retryAtUtc, publishedRow.LastAttemptAtUtc);
        Assert.Equal(retryAtUtc, publishedRow.PublishedAtUtc);
        Assert.Null(publishedRow.LastError);
    }

    private static void AssertEnvelopeEquivalent(
        IntegrationMessageEnvelope expected,
        IntegrationMessageEnvelope actual)
    {
        Assert.Equal(expected.MessageId, actual.MessageId);
        Assert.Equal(expected.MessageType, actual.MessageType);
        Assert.Equal(expected.OccurredAtUtc, actual.OccurredAtUtc);
        Assert.Equal(expected.CorrelationId, actual.CorrelationId);
        Assert.Equal(expected.CausationId, actual.CausationId);

        var expectedPayload = JsonNode.Parse(expected.PayloadJson);
        var actualPayload = JsonNode.Parse(actual.PayloadJson);

        Assert.True(
            JsonNode.DeepEquals(expectedPayload, actualPayload),
            $"Expected JSON payload '{expected.PayloadJson}' but received '{actual.PayloadJson}'.");
    }

    private static PaymentsOutboxDispatchCycle CreateCycle(
        string connectionString,
        IMessageTransport transport,
        DateTimeOffset nowUtc,
        TimeSpan retryDelay)
    {
        return new PaymentsOutboxDispatchCycle(
            new TestPaymentsDbContextFactory(connectionString),
            transport,
            new FixedTimeProvider(nowUtc),
            new PaymentsOutboxWorkerOptions(
                batchSize: 10,
                leaseDuration: TimeSpan.FromMinutes(1),
                retryDelay: retryDelay,
                pollInterval: TimeSpan.FromMilliseconds(100)));
    }

    private static async Task AddOutboxMessageAsync(
        string connectionString,
        IntegrationMessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        var store = new EfPaymentsOutboxStore(dbContext);

        await store.AddAsync(envelope, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_payments_outbox_test")
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

    private static async Task<OutboxState> ReadOutboxStateAsync(
        string connectionString,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    available_at_utc,
                    delivery_attempt_count,
                    last_attempt_at_utc,
                    lock_token,
                    locked_until_utc,
                    published_at_utc,
                    last_error
                FROM payments.outbox_messages
                WHERE message_id = @message_id;
                """,
                connection);
        command.Parameters.AddWithValue("message_id", messageId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        return new OutboxState(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetInt32(1),
            reader.IsDBNull(2)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetGuid(3),
            reader.IsDBNull(4)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6)
                ? null
                : reader.GetString(6));
    }

    private sealed record OutboxState(
        DateTimeOffset AvailableAtUtc,
        int DeliveryAttemptCount,
        DateTimeOffset? LastAttemptAtUtc,
        Guid? LockToken,
        DateTimeOffset? LockedUntilUtc,
        DateTimeOffset? PublishedAtUtc,
        string? LastError);

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
            PaymentsOutboxDispatchCycleTests.CreateDbContext(
                _connectionString);

        public Task<PaymentsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        private readonly bool _fail;

        public RecordingTransport(bool fail = false)
        {
            _fail = fail;
        }

        public List<IntegrationMessageEnvelope> Published { get; } = [];

        public Task PublishAsync(
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_fail)
            {
                throw new TimeoutException(
                    "Deterministic Payments outbox transport failure.");
            }

            Published.Add(message);
            return Task.CompletedTask;
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
