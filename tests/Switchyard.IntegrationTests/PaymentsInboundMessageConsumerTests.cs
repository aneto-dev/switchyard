using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PaymentsInboundMessageConsumerTests
{
    [Fact]
    public async Task AuthorisedMessagePersistsOneInboxAndOutboxOutcomeAcrossReplay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var incoming = CreateEnvelope(
            requestId,
            orderId,
            1299.99m,
            "GBP",
            now.AddMinutes(10),
            now);
        var consumer = CreateConsumer(
            postgres.GetConnectionString(),
            provider,
            now);

        await consumer.ConsumeAsync(incoming, cancellationToken);
        await consumer.ConsumeAsync(incoming, cancellationToken);

        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, provider.UniqueAuthorisationCount);
        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "inbox_messages",
                cancellationToken));
        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "outbox_messages",
                cancellationToken));
        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "payment_intents",
                cancellationToken));

        var outbox = await ReadSingleOutboxAsync(
            postgres.GetConnectionString(),
            cancellationToken);

        Assert.Equal(PaymentAuthorisedV1.MessageType, outbox.MessageType);
        Assert.Equal(orderId, outbox.CorrelationId);
        Assert.Equal(incoming.MessageId, outbox.CausationId);

        var payload =
            JsonSerializer.Deserialize<PaymentAuthorisedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(requestId, payload.RequestId);
        Assert.Equal(orderId, payload.OrderId);
        Assert.Equal(1299.99m, payload.Amount);
        Assert.Equal("GBP", payload.Currency);
        Assert.False(string.IsNullOrWhiteSpace(payload.ProviderReference));
    }

    [Fact]
    public async Task ExpiredReservationFailsWithoutCreatingPaymentOrCallingProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 10, 17, 15, 10, 0, TimeSpan.Zero);
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var incoming = CreateEnvelope(
            requestId,
            orderId,
            250m,
            "GBP",
            now.AddSeconds(-1),
            now.AddMinutes(-1));

        await CreateConsumer(
                postgres.GetConnectionString(),
                provider,
                now)
            .ConsumeAsync(incoming, cancellationToken);

        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(
            0,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "payment_intents",
                cancellationToken));

        var outbox = await ReadSingleOutboxAsync(
            postgres.GetConnectionString(),
            cancellationToken);
        var payload =
            JsonSerializer.Deserialize<PaymentAuthorisationFailedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.Equal(PaymentAuthorisationFailedV1.MessageType, outbox.MessageType);
        Assert.NotNull(payload);
        Assert.Null(payload.PaymentId);
        Assert.Equal(
            PaymentAuthorisationFailedV1.ReservationExpiredReason,
            payload.Reason);
    }

    [Fact]
    public async Task MissingReservationValidityFailsClosedWithoutProviderSideEffect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 10, 17, 15, 20, 0, TimeSpan.Zero);
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);
        var incoming = CreateEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            75m,
            "GBP",
            reservationValidUntilUtc: null,
            now);

        await CreateConsumer(
                postgres.GetConnectionString(),
                provider,
                now)
            .ConsumeAsync(incoming, cancellationToken);

        Assert.Equal(0, provider.InvocationCount);

        var outbox = await ReadSingleOutboxAsync(
            postgres.GetConnectionString(),
            cancellationToken);
        var payload =
            JsonSerializer.Deserialize<PaymentAuthorisationFailedV1>(
                outbox.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(
            PaymentAuthorisationFailedV1.ReservationValidityMissingReason,
            payload.Reason);
    }

    [Fact]
    public async Task ConflictingMessageIdentityIsRejectedBeforeProviderSideEffect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 10, 17, 15, 22, 0, TimeSpan.Zero);
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);
        var messageId = Guid.NewGuid();
        var first = CreateEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            50m,
            "GBP",
            now.AddSeconds(-1),
            now.AddMinutes(-1),
            messageId);
        var consumer = CreateConsumer(
            postgres.GetConnectionString(),
            provider,
            now);

        await consumer.ConsumeAsync(first, cancellationToken);

        var conflicting = CreateEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            60m,
            "GBP",
            now.AddMinutes(10),
            now,
            messageId);

        await Assert.ThrowsAsync<InboxMessageConflictException>(
            () => consumer.ConsumeAsync(
                conflicting,
                cancellationToken));

        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(
            0,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "payment_intents",
                cancellationToken));
        Assert.Equal(
            1,
            await CountRowsAsync(
                postgres.GetConnectionString(),
                "outbox_messages",
                cancellationToken));
    }

    [Fact]
    public async Task BusyInboxClaimWaitsForLeaseAndRecoversClaimedProviderInvocationAsIndeterminate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var connectionString =
            postgres.GetConnectionString();
        var requestedAtUtc =
            TimeProvider.System.GetUtcNow();
        var requestId =
            Guid.NewGuid();
        var orderId =
            Guid.NewGuid();
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);
        var incoming =
            CreateEnvelope(
                requestId,
                orderId,
                349m,
                "GBP",
                requestedAtUtc.AddMinutes(10),
                requestedAtUtc);

        await using (var paymentDbContext =
            CreateDbContext(connectionString))
        {
            var store =
                new EfPaymentAuthorisationStore(
                    paymentDbContext);

            await store.BeginAsync(
                new PaymentAuthorisationRequest(
                    requestId,
                    orderId,
                    new Switchyard.Payments.Domain.Payments.PaymentAmount(
                        349m,
                        "GBP"),
                    requestedAtUtc),
                cancellationToken);

            var invocationClaim =
                await store.ClaimProviderInvocationAsync(
                    requestId,
                    TimeProvider.System.GetUtcNow(),
                    cancellationToken);

            Assert.True(
                invocationClaim.Acquired);
        }

        await using (var inboxDbContext =
            CreateDbContext(connectionString))
        {
            var inbox =
                new EfPaymentsInboxMessageProcessor(
                    inboxDbContext,
                    TimeProvider.System);

            var inboxClaim =
                await inbox.ClaimAsync(
                    "payments-authorisation",
                    incoming,
                    TimeSpan.FromSeconds(3),
                    cancellationToken);

            Assert.Equal(
                PaymentsInboxClaimStatus.Claimed,
                inboxClaim.Status);
        }

        await using (var busyCheckDbContext =
            CreateDbContext(connectionString))
        {
            var inbox =
                new EfPaymentsInboxMessageProcessor(
                    busyCheckDbContext,
                    TimeProvider.System);

            var busyClaim =
                await inbox.ClaimAsync(
                    "payments-authorisation",
                    incoming,
                    TimeSpan.FromMinutes(2),
                    cancellationToken);

            Assert.Equal(
                PaymentsInboxClaimStatus.Busy,
                busyClaim.Status);
            Assert.NotNull(
                busyClaim.ProcessingLockedUntilUtc);
            Assert.True(
                busyClaim.ProcessingLockedUntilUtc!.Value -
                TimeProvider.System.GetUtcNow() >
                TimeSpan.FromSeconds(2));
        }

        var consumer =
            new PaymentsInboundMessageConsumer(
                new TestPaymentsDbContextFactory(
                    connectionString),
                provider,
                TimeProvider.System);

        await consumer.ConsumeAsync(
            incoming,
            cancellationToken);

        Assert.Equal(
            0,
            provider.InvocationCount);

        var payment =
            await ReadAuthorisationStatusAsync(
                connectionString,
                requestId,
                cancellationToken);

        Assert.Equal(
            (int)PaymentAuthorisationStatus.Indeterminate,
            payment);

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

        var outbox =
            await ReadSingleOutboxAsync(
                connectionString,
                cancellationToken);

        Assert.Equal(
            PaymentAuthorisationIndeterminateV1.MessageType,
            outbox.MessageType);
    }

    [Fact]
    public async Task PendingAttemptThatReappearsAfterDeadlineBecomesIndeterminateWithoutBlindRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var requestedAtUtc =
            new DateTimeOffset(2026, 10, 17, 15, 25, 0, TimeSpan.Zero);
        var observedAtUtc = requestedAtUtc.AddMinutes(2);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);

        await using (var dbContext =
            CreateDbContext(postgres.GetConnectionString()))
        {
            var store = new EfPaymentAuthorisationStore(dbContext);

            await store.BeginAsync(
                new PaymentAuthorisationRequest(
                    requestId,
                    orderId,
                    new Switchyard.Payments.Domain.Payments.PaymentAmount(
                        499m,
                        "GBP"),
                    requestedAtUtc),
                cancellationToken);
        }

        var incoming = CreateEnvelope(
            requestId,
            orderId,
            499m,
            "GBP",
            requestedAtUtc.AddMinutes(1),
            requestedAtUtc);

        await CreateConsumer(
                postgres.GetConnectionString(),
                provider,
                observedAtUtc)
            .ConsumeAsync(incoming, cancellationToken);

        Assert.Equal(0, provider.InvocationCount);

        var payment = await ReadAuthorisationStatusAsync(
            postgres.GetConnectionString(),
            requestId,
            cancellationToken);
        Assert.Equal(
            (int)PaymentAuthorisationStatus.Indeterminate,
            payment);

        var outbox = await ReadSingleOutboxAsync(
            postgres.GetConnectionString(),
            cancellationToken);
        Assert.Equal(
            PaymentAuthorisationIndeterminateV1.MessageType,
            outbox.MessageType);
    }

    [Fact]
    public async Task ResolvedPaymentCanFinaliseMessagingAfterDeadlineWithoutSecondProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var authorisedAtUtc =
            new DateTimeOffset(2026, 10, 17, 15, 30, 0, TimeSpan.Zero);
        var redeliveredAtUtc = authorisedAtUtc.AddMinutes(3);
        var requestId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var provider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Authorise);

        await using (var dbContext =
            CreateDbContext(postgres.GetConnectionString()))
        {
            var handler =
                new AuthorisePaymentHandler(
                    new EfPaymentAuthorisationStore(dbContext),
                    provider,
                    new FixedTimeProvider(authorisedAtUtc));

            await handler.HandleAsync(
                new AuthorisePaymentCommand(
                    requestId,
                    orderId,
                    899m,
                    "GBP"),
                cancellationToken);
        }

        Assert.Equal(1, provider.InvocationCount);

        var incoming = CreateEnvelope(
            requestId,
            orderId,
            899m,
            "GBP",
            authorisedAtUtc.AddMinutes(1),
            authorisedAtUtc);

        await CreateConsumer(
                postgres.GetConnectionString(),
                provider,
                redeliveredAtUtc)
            .ConsumeAsync(incoming, cancellationToken);

        Assert.Equal(1, provider.InvocationCount);

        var outbox = await ReadSingleOutboxAsync(
            postgres.GetConnectionString(),
            cancellationToken);
        Assert.Equal(PaymentAuthorisedV1.MessageType, outbox.MessageType);
    }

    [Fact]
    public async Task DeclinedAndIndeterminateResultsPublishExplicitTerminalOutcomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 10, 17, 15, 40, 0, TimeSpan.Zero);

        var declinedProvider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Decline);
        var declined = CreateEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            120m,
            "GBP",
            now.AddMinutes(10),
            now);

        await CreateConsumer(
                postgres.GetConnectionString(),
                declinedProvider,
                now)
            .ConsumeAsync(declined, cancellationToken);

        var first = await ReadOutboxByCausationAsync(
            postgres.GetConnectionString(),
            declined.MessageId,
            cancellationToken);
        Assert.Equal(PaymentAuthorisationFailedV1.MessageType, first.MessageType);

        var indeterminateProvider =
            new SimulatedPaymentAuthorisationProvider(
                SimulatedPaymentAuthorisationScenario.Indeterminate);
        var indeterminate = CreateEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            130m,
            "GBP",
            now.AddMinutes(10),
            now.AddSeconds(1));

        await CreateConsumer(
                postgres.GetConnectionString(),
                indeterminateProvider,
                now.AddSeconds(1))
            .ConsumeAsync(indeterminate, cancellationToken);
        await CreateConsumer(
                postgres.GetConnectionString(),
                indeterminateProvider,
                now.AddSeconds(2))
            .ConsumeAsync(indeterminate, cancellationToken);

        Assert.Equal(1, indeterminateProvider.InvocationCount);

        var second = await ReadOutboxByCausationAsync(
            postgres.GetConnectionString(),
            indeterminate.MessageId,
            cancellationToken);
        Assert.Equal(
            PaymentAuthorisationIndeterminateV1.MessageType,
            second.MessageType);
    }

    private static PaymentsInboundMessageConsumer CreateConsumer(
        string connectionString,
        SimulatedPaymentAuthorisationProvider provider,
        DateTimeOffset nowUtc)
    {
        return new PaymentsInboundMessageConsumer(
            new TestPaymentsDbContextFactory(connectionString),
            provider,
            new FixedTimeProvider(nowUtc));
    }

    private static IntegrationMessageEnvelope CreateEnvelope(
        Guid requestId,
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset? reservationValidUntilUtc,
        DateTimeOffset occurredAtUtc,
        Guid? messageId = null)
    {
        var command =
            new AuthorisePaymentV1(
                requestId,
                orderId,
                amount,
                currency,
                reservationValidUntilUtc);

        return new IntegrationMessageEnvelope(
            messageId ?? Guid.NewGuid(),
            AuthorisePaymentV1.MessageType,
            JsonSerializer.Serialize(
                command,
                JsonSerializerOptions.Web),
            occurredAtUtc,
            orderId,
            causationId: null);
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
                .WithDatabase("switchyard_payments_messaging_test")
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

    private static async Task<int> CountRowsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        if (tableName is not (
            "inbox_messages" or
            "outbox_messages" or
            "payment_intents"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                $"SELECT count(*) FROM payments.{tableName};",
                connection);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> ReadAuthorisationStatusAsync(
        string connectionString,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT status
                FROM payments.authorisation_attempts
                WHERE request_id = @request_id;
                """,
                connection);
        command.Parameters.AddWithValue("request_id", requestId);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<OutboxRow> ReadSingleOutboxAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    message_type,
                    payload::text,
                    correlation_id,
                    causation_id
                FROM payments.outbox_messages;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        var row =
            new OutboxRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3)
                    ? null
                    : reader.GetGuid(3));

        Assert.False(await reader.ReadAsync(cancellationToken));
        return row;
    }

    private static async Task<OutboxRow> ReadOutboxByCausationAsync(
        string connectionString,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    message_type,
                    payload::text,
                    correlation_id,
                    causation_id
                FROM payments.outbox_messages
                WHERE causation_id = @causation_id;
                """,
                connection);
        command.Parameters.AddWithValue("causation_id", causationId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        var row =
            new OutboxRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3)
                    ? null
                    : reader.GetGuid(3));

        Assert.False(await reader.ReadAsync(cancellationToken));
        return row;
    }

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
            _connectionString = connectionString;
        }

        public PaymentsDbContext CreateDbContext() =>
            PaymentsInboundMessageConsumerTests.CreateDbContext(
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

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
