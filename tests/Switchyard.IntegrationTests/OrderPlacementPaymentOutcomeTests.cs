using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Switchyard.Ordering.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OrderPlacementPaymentOutcomeTests
{
    [Fact]
    public async Task AuthorisedPaymentConfirmsOrderAndProcessIdempotently()
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

        var now =
            new DateTimeOffset(
                2026,
                10,
                18,
                10,
                0,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateAwaitingPaymentOrderAsync(
                connectionString,
                now,
                "payment-authorised",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                now.AddSeconds(20));

        var envelope =
            CreateAuthorisedEnvelope(
                setup,
                now.AddSeconds(15));

        await consumer.ConsumeAsync(
            envelope,
            cancellationToken);

        await consumer.ConsumeAsync(
            envelope,
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.Confirmed,
            process.State);
        Assert.Equal(
            OrderStatus.Confirmed,
            order.Status);
        Assert.Empty(
            await ReadReleaseCommandsAsync(
                connectionString,
                setup.OrderId,
                cancellationToken));
    }

    [Fact]
    public async Task DeclinedPaymentStartsInventoryCompensationOnlyOnceAcrossLogicalReplay()
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

        var now =
            new DateTimeOffset(
                2026,
                10,
                18,
                10,
                10,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateAwaitingPaymentOrderAsync(
                connectionString,
                now,
                "payment-declined",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                now.AddSeconds(20));

        await consumer.ConsumeAsync(
            CreateFailedEnvelope(
                setup,
                PaymentAuthorisationFailedV1.DeclinedReason,
                now.AddSeconds(15)),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateFailedEnvelope(
                setup,
                PaymentAuthorisationFailedV1.DeclinedReason,
                now.AddSeconds(15),
                Guid.NewGuid()),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.CompensatingInventory,
            process.State);
        Assert.Equal(
            OrderStatus.Pending,
            order.Status);
        Assert.All(
            process.Lines,
            line =>
                Assert.Equal(
                    OrderPlacementLineState.AwaitingRelease,
                    line.State));

        var releases =
            await ReadReleaseCommandsAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            setup.Reservations.Count,
            releases.Count);
    }

    [Fact]
    public async Task ReservationValidityFailureAlsoStartsInventoryCompensationWithoutPaymentIdentity()
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

        var now =
            new DateTimeOffset(
                2026,
                10,
                18,
                10,
                20,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateAwaitingPaymentOrderAsync(
                connectionString,
                now,
                "payment-validity-failed",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                now.AddSeconds(20));

        await consumer.ConsumeAsync(
            CreateFailedEnvelope(
                setup,
                PaymentAuthorisationFailedV1.ReservationExpiredReason,
                now.AddSeconds(15)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.CompensatingInventory,
            process.State);

        Assert.Equal(
            setup.Reservations.Count,
            (await ReadReleaseCommandsAsync(
                connectionString,
                setup.OrderId,
                cancellationToken)).Count);
    }

    [Fact]
    public async Task IndeterminatePaymentPausesThenLaterAuthorisedOutcomeConfirms()
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

        var now =
            new DateTimeOffset(
                2026,
                10,
                18,
                10,
                30,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateAwaitingPaymentOrderAsync(
                connectionString,
                now,
                "payment-indeterminate-authorised",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                now.AddSeconds(30));

        await consumer.ConsumeAsync(
            CreateIndeterminateEnvelope(
                setup,
                now.AddSeconds(15)),
            cancellationToken);

        var paused =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.PaymentIndeterminate,
            paused.State);
        Assert.Empty(
            await ReadReleaseCommandsAsync(
                connectionString,
                setup.OrderId,
                cancellationToken));

        await consumer.ConsumeAsync(
            CreateAuthorisedEnvelope(
                setup,
                now.AddSeconds(25)),
            cancellationToken);

        var confirmed =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.Confirmed,
            confirmed.State);
        Assert.Equal(
            OrderStatus.Confirmed,
            order.Status);
    }

    [Fact]
    public async Task WrongPaymentRequestIsRejectedWithoutChangingOrderingState()
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

        var now =
            new DateTimeOffset(
                2026,
                10,
                18,
                10,
                40,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateAwaitingPaymentOrderAsync(
                connectionString,
                now,
                "payment-wrong-request",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                now.AddSeconds(20));

        var invalid =
            CreateAuthorisedEnvelope(
                setup with
                {
                    PaymentRequestId = Guid.NewGuid()
                },
                now.AddSeconds(15));

        await Assert.ThrowsAsync<NonRetryableIntegrationMessageException>(
            () =>
                consumer.ConsumeAsync(
                    invalid,
                    cancellationToken));

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.AwaitingPayment,
            process.State);
        Assert.Equal(
            OrderStatus.Pending,
            order.Status);
        Assert.Empty(
            await ReadReleaseCommandsAsync(
                connectionString,
                setup.OrderId,
                cancellationToken));
    }

    private static OrderingInboundMessageConsumer CreateConsumer(
        string connectionString,
        DateTimeOffset now)
    {
        var timeProvider =
            new FixedTimeProvider(
                now);

        return new OrderingInboundMessageConsumer(
            new TestOrderingDbContextFactory(
                connectionString),
            timeProvider,
            new IOrderingInboundMessageRoute[]
            {
                new InventoryReservedInboundMessageRoute(
                    timeProvider),
                new PaymentAuthorisedInboundMessageRoute(
                    timeProvider),
                new PaymentAuthorisationFailedInboundMessageRoute(
                    timeProvider),
                new PaymentAuthorisationIndeterminateInboundMessageRoute(
                    timeProvider)
            });
    }

    private static async Task<PaymentSetup> CreateAwaitingPaymentOrderAsync(
        string connectionString,
        DateTimeOffset now,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guid orderId;

        await using (var dbContext =
                     CreateDbContext(
                         connectionString))
        {
            var handler =
                new CreatePendingOrderHandler(
                    new EfOrderRepository(dbContext),
                    new EfOrderRequestRepository(dbContext),
                    new EfOrderPlacementProcessRepository(dbContext),
                    new EfOrderingUnitOfWork(dbContext),
                    new EfOrderingOutboxStore(dbContext),
                    new PostgresOrderNumberGenerator(dbContext),
                    new FixedTimeProvider(now));

            var result =
                await handler.HandleAsync(
                    new CreatePendingOrderCommand(
                        idempotencyKey,
                        new[]
                        {
                            new CreatePendingOrderLine(
                                "BIKE-001",
                                "Road Bike",
                                1,
                                1299.99m,
                                "GBP"),
                            new CreatePendingOrderLine(
                                "HELMET-001",
                                "Helmet",
                                1,
                                99.99m,
                                "GBP")
                        }),
                    cancellationToken);

            orderId =
                result.OrderId;
        }

        var reserveCommands =
            await ReadReserveCommandsAsync(
                connectionString,
                orderId,
                cancellationToken);

        var reservations =
            reserveCommands
                .Select(
                    command =>
                        new ReservationSetup(
                            command,
                            Guid.NewGuid()))
                .ToArray();

        var consumer =
            CreateConsumer(
                connectionString,
                now.AddSeconds(10));

        for (var index = 0; index < reservations.Length; index++)
        {
            var reservation =
                reservations[index];

            var reservedAtUtc =
                now.AddSeconds(
                    index + 1);

            await consumer.ConsumeAsync(
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    InventoryReservedV1.MessageType,
                    JsonSerializer.Serialize(
                        new InventoryReservedV1(
                            reservation.Command.RequestId,
                            reservation.Command.OrderId,
                            reservation.Command.OrderLineId,
                            reservation.Command.SkuCode,
                            reservation.Command.Quantity,
                            reservation.ReservationId,
                            reservedAtUtc,
                            reservedAtUtc.AddMinutes(15)),
                        JsonSerializerOptions.Web),
                    reservedAtUtc,
                    orderId,
                    reservation.CommandMessageId),
                cancellationToken);
        }

        var process =
            await ReadProcessAsync(
                connectionString,
                orderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.AwaitingPayment,
            process.State);

        var paymentRequestId =
            process.PaymentAuthorisationRequestId ??
            throw new InvalidOperationException(
                "AwaitingPayment process is missing payment request identity.");

        return new PaymentSetup(
            orderId,
            paymentRequestId,
            1399.98m,
            "GBP",
            reservations);
    }

    private static IntegrationMessageEnvelope CreateAuthorisedEnvelope(
        PaymentSetup setup,
        DateTimeOffset occurredAtUtc,
        Guid? messageId = null)
    {
        return new IntegrationMessageEnvelope(
            messageId ?? Guid.NewGuid(),
            PaymentAuthorisedV1.MessageType,
            JsonSerializer.Serialize(
                new PaymentAuthorisedV1(
                    setup.PaymentRequestId,
                    setup.OrderId,
                    Guid.NewGuid(),
                    setup.Amount,
                    setup.Currency,
                    "sim-auth-001",
                    occurredAtUtc),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            setup.OrderId,
            setup.PaymentRequestId);
    }

    private static IntegrationMessageEnvelope CreateFailedEnvelope(
        PaymentSetup setup,
        string reason,
        DateTimeOffset occurredAtUtc,
        Guid? messageId = null)
    {
        var declined =
            string.Equals(
                reason,
                PaymentAuthorisationFailedV1.DeclinedReason,
                StringComparison.Ordinal);

        return new IntegrationMessageEnvelope(
            messageId ?? Guid.NewGuid(),
            PaymentAuthorisationFailedV1.MessageType,
            JsonSerializer.Serialize(
                new PaymentAuthorisationFailedV1(
                    setup.PaymentRequestId,
                    setup.OrderId,
                    declined
                        ? Guid.NewGuid()
                        : null,
                    setup.Amount,
                    setup.Currency,
                    reason,
                    declined
                        ? "sim-decline-001"
                        : null,
                    occurredAtUtc,
                    string.Equals(
                        reason,
                        PaymentAuthorisationFailedV1.ReservationValidityMissingReason,
                        StringComparison.Ordinal)
                        ? null
                        : occurredAtUtc.AddMinutes(-1)),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            setup.OrderId,
            setup.PaymentRequestId);
    }

    private static IntegrationMessageEnvelope CreateIndeterminateEnvelope(
        PaymentSetup setup,
        DateTimeOffset occurredAtUtc)
    {
        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            PaymentAuthorisationIndeterminateV1.MessageType,
            JsonSerializer.Serialize(
                new PaymentAuthorisationIndeterminateV1(
                    setup.PaymentRequestId,
                    setup.OrderId,
                    Guid.NewGuid(),
                    setup.Amount,
                    setup.Currency,
                    ProviderReference: null,
                    occurredAtUtc),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            setup.OrderId,
            setup.PaymentRequestId);
    }

    private static async Task<IReadOnlyList<ReserveCommandRow>> ReadReserveCommandsAsync(
        string connectionString,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var rows =
            new List<ReserveCommandRow>();

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    message_id,
                    payload::text
                FROM ordering.outbox_messages
                WHERE message_type = 'inventory.command.reserve.v1'
                  AND correlation_id = @order_id
                ORDER BY occurred_at_utc, message_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var payload =
                JsonSerializer.Deserialize<ReserveInventoryV1>(
                    reader.GetString(1),
                    JsonSerializerOptions.Web);

            Assert.NotNull(
                payload);

            rows.Add(
                new ReserveCommandRow(
                    reader.GetGuid(0),
                    payload));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ReleaseInventoryReservationV1>> ReadReleaseCommandsAsync(
        string connectionString,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var releases =
            new List<ReleaseInventoryReservationV1>();

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT payload::text
                FROM ordering.outbox_messages
                WHERE message_type = 'inventory.command.release.v1'
                  AND correlation_id = @order_id
                ORDER BY occurred_at_utc, message_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var release =
                JsonSerializer.Deserialize<ReleaseInventoryReservationV1>(
                    reader.GetString(0),
                    JsonSerializerOptions.Web);

            Assert.NotNull(
                release);

            releases.Add(
                release);
        }

        return releases;
    }

    private static async Task<OrderPlacementProcess> ReadProcessAsync(
        string connectionString,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var process =
            await new EfOrderPlacementProcessRepository(
                dbContext)
                .GetByOrderIdAsync(
                    new OrderId(orderId),
                    cancellationToken);

        return Assert.IsType<OrderPlacementProcess>(
            process);
    }

    private static async Task<Order> ReadOrderAsync(
        string connectionString,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var order =
            await new EfOrderRepository(
                dbContext)
                .GetByIdAsync(
                    new OrderId(orderId),
                    cancellationToken);

        return Assert.IsType<Order>(
            order);
    }

    private static OrderingDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<OrderingDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        return new OrderingDbContext(
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
                    "switchyard_ordering_payment_outcomes_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        return postgres;
    }

    private sealed record PaymentSetup(
        Guid OrderId,
        Guid PaymentRequestId,
        decimal Amount,
        string Currency,
        IReadOnlyList<ReservationSetup> Reservations);

    private sealed record ReservationSetup(
        ReserveCommandRow Reserve,
        Guid ReservationId)
    {
        public ReserveInventoryV1 Command =>
            Reserve.Command;

        public Guid CommandMessageId =>
            Reserve.MessageId;
    }

    private sealed record ReserveCommandRow(
        Guid MessageId,
        ReserveInventoryV1 Command);

    private sealed class TestOrderingDbContextFactory :
        IDbContextFactory<OrderingDbContext>
    {
        private readonly string _connectionString;

        public TestOrderingDbContextFactory(
            string connectionString)
        {
            _connectionString =
                connectionString;
        }

        public OrderingDbContext CreateDbContext() =>
            OrderPlacementPaymentOutcomeTests.CreateDbContext(
                _connectionString);

        public Task<OrderingDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
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
