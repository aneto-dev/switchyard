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

public sealed class OrderPlacementInventoryOutcomeTests
{
    [Fact]
    public async Task AllReservedTransitionsToAwaitingPaymentAndEmitsOnePaymentCommand()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                24,
                14,
                0,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-success",
                cancellationToken);

        var outcomeAtUtc =
            acceptedAtUtc.AddSeconds(10);

        var consumer =
            CreateConsumer(
                connectionString,
                outcomeAtUtc);

        var firstReservationId =
            Guid.NewGuid();
        var secondReservationId =
            Guid.NewGuid();

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                setup.ReserveCommands[0],
                firstReservationId,
                outcomeAtUtc),
            cancellationToken);

        var afterFirst =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.AwaitingInventory,
            afterFirst.State);

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                setup.ReserveCommands[1],
                secondReservationId,
                outcomeAtUtc.AddSeconds(1)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.AwaitingPayment,
            process.State);
        Assert.NotNull(
            process.PaymentAuthorisationRequestId);

        Assert.All(
            process.Lines,
            line =>
                Assert.Equal(
                    OrderPlacementLineState.Reserved,
                    line.State));

        var paymentMessages =
            await ReadOutboxByTypeAsync(
                connectionString,
                AuthorisePaymentV1.MessageType,
                cancellationToken);

        var paymentMessage =
            Assert.Single(paymentMessages);

        var payment =
            JsonSerializer.Deserialize<AuthorisePaymentV1>(
                paymentMessage.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payment);
        Assert.Equal(
            process.PaymentAuthorisationRequestId,
            payment.RequestId);
        Assert.Equal(
            paymentMessage.MessageId,
            payment.RequestId);
        Assert.Equal(
            setup.OrderId,
            payment.OrderId);
        Assert.Equal(
            1399.98m,
            payment.Amount);
        Assert.Equal(
            "GBP",
            payment.Currency);
        Assert.Equal(
            setup.OrderId,
            paymentMessage.CorrelationId);

        var finalReservation =
            setup.ReserveCommands[1];

        Assert.Equal(
            finalReservation.EventMessageId,
            paymentMessage.CausationId);

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                finalReservation,
                secondReservationId,
                outcomeAtUtc.AddSeconds(2),
                Guid.NewGuid()),
            cancellationToken);

        Assert.Single(
            await ReadOutboxByTypeAsync(
                connectionString,
                AuthorisePaymentV1.MessageType,
                cancellationToken));
    }

    [Fact]
    public async Task ConcurrentReservedOutcomesSerializeAndEmitOnePaymentCommand()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                24,
                14,
                5,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-concurrent-success",
                cancellationToken);

        var timeProvider =
            new FixedTimeProvider(
                acceptedAtUtc.AddSeconds(10));

        var route =
            new CoordinatedRoute(
                new InventoryReservedInboundMessageRoute(
                    timeProvider),
                expectedCalls: 2);

        var consumer =
            new OrderingInboundMessageConsumer(
                new TestOrderingDbContextFactory(
                    connectionString),
                timeProvider,
                new IOrderingInboundMessageRoute[]
                {
                    route
                });

        var first =
            consumer.ConsumeAsync(
                CreateReservedEnvelope(
                    setup.ReserveCommands[0],
                    Guid.NewGuid(),
                    acceptedAtUtc.AddSeconds(10)),
                cancellationToken);

        var second =
            consumer.ConsumeAsync(
                CreateReservedEnvelope(
                    setup.ReserveCommands[1],
                    Guid.NewGuid(),
                    acceptedAtUtc.AddSeconds(10)),
                cancellationToken);

        await Task.WhenAll(
            first,
            second);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.AwaitingPayment,
            process.State);
        Assert.NotNull(
            process.PaymentAuthorisationRequestId);

        Assert.All(
            process.Lines,
            line =>
                Assert.Equal(
                    OrderPlacementLineState.Reserved,
                    line.State));

        var paymentMessages =
            await ReadOutboxByTypeAsync(
                connectionString,
                AuthorisePaymentV1.MessageType,
                cancellationToken);

        var paymentMessage =
            Assert.Single(
                paymentMessages);

        var payment =
            JsonSerializer.Deserialize<AuthorisePaymentV1>(
                paymentMessage.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payment);
        Assert.Equal(
            process.PaymentAuthorisationRequestId,
            payment.RequestId);
    }

    [Fact]
    public async Task RejectionAfterReservationStartsCompensationWithoutPayment()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                24,
                14,
                10,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-rejection",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                acceptedAtUtc.AddSeconds(10));

        var reservationId =
            Guid.NewGuid();

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                setup.ReserveCommands[0],
                reservationId,
                acceptedAtUtc.AddSeconds(10)),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateRejectedEnvelope(
                setup.ReserveCommands[1],
                acceptedAtUtc.AddSeconds(11)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.CompensatingInventory,
            process.State);

        Assert.Contains(
            process.Lines,
            line =>
                line.OrderLineId.Value ==
                    setup.ReserveCommands[0].Command.OrderLineId &&
                line.State ==
                    OrderPlacementLineState.AwaitingRelease &&
                line.ReservationId ==
                    reservationId);

        Assert.Contains(
            process.Lines,
            line =>
                line.OrderLineId.Value ==
                    setup.ReserveCommands[1].Command.OrderLineId &&
                line.State ==
                    OrderPlacementLineState.Rejected);

        Assert.Empty(
            await ReadOutboxByTypeAsync(
                connectionString,
                AuthorisePaymentV1.MessageType,
                cancellationToken));

        var releaseMessage =
            Assert.Single(
                await ReadOutboxByTypeAsync(
                    connectionString,
                    ReleaseInventoryReservationV1.MessageType,
                    cancellationToken));

        var release =
            JsonSerializer.Deserialize<ReleaseInventoryReservationV1>(
                releaseMessage.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(release);
        Assert.Equal(
            setup.OrderId,
            release.OrderId);
        Assert.Equal(
            setup.ReserveCommands[0].Command.RequestId,
            release.RequestId);
        Assert.Equal(
            reservationId,
            release.ReservationId);
        Assert.Equal(
            ReleaseInventoryReservationV1.CompensationReason,
            release.Reason);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderStatus.Pending,
            order.Status);
    }

    [Fact]
    public async Task LateReservationAfterRejectionIsImmediatelyScheduledForRelease()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                24,
                14,
                20,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-late-reserve",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                acceptedAtUtc.AddSeconds(10));

        await consumer.ConsumeAsync(
            CreateRejectedEnvelope(
                setup.ReserveCommands[0],
                acceptedAtUtc.AddSeconds(10)),
            cancellationToken);

        var afterReject =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.CompensatingInventory,
            afterReject.State);

        Assert.Empty(
            await ReadOutboxByTypeAsync(
                connectionString,
                ReleaseInventoryReservationV1.MessageType,
                cancellationToken));

        var lateReservationId =
            Guid.NewGuid();

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                setup.ReserveCommands[1],
                lateReservationId,
                acceptedAtUtc.AddSeconds(11)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.CompensatingInventory,
            process.State);

        Assert.Contains(
            process.Lines,
            line =>
                line.OrderLineId.Value ==
                    setup.ReserveCommands[1].Command.OrderLineId &&
                line.State ==
                    OrderPlacementLineState.AwaitingRelease &&
                line.ReservationId ==
                    lateReservationId);

        var releaseMessage =
            Assert.Single(
                await ReadOutboxByTypeAsync(
                    connectionString,
                    ReleaseInventoryReservationV1.MessageType,
                    cancellationToken));

        Assert.Equal(
            setup.ReserveCommands[1].EventMessageId,
            releaseMessage.CausationId);

        Assert.Empty(
            await ReadOutboxByTypeAsync(
                connectionString,
                AuthorisePaymentV1.MessageType,
                cancellationToken));
    }

    [Fact]
    public async Task AllRejectedFailsOrderWithoutPaymentOrCompensationCommand()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                24,
                14,
                30,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-all-rejected",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                acceptedAtUtc.AddSeconds(10));

        await consumer.ConsumeAsync(
            CreateRejectedEnvelope(
                setup.ReserveCommands[0],
                acceptedAtUtc.AddSeconds(10)),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateRejectedEnvelope(
                setup.ReserveCommands[1],
                acceptedAtUtc.AddSeconds(11)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.Failed,
            process.State);

        Assert.All(
            process.Lines,
            line =>
                Assert.Equal(
                    OrderPlacementLineState.Rejected,
                    line.State));

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderStatus.Failed,
            order.Status);

        Assert.Empty(
            await ReadOutboxByTypeAsync(
                connectionString,
                AuthorisePaymentV1.MessageType,
                cancellationToken));

        Assert.Empty(
            await ReadOutboxByTypeAsync(
                connectionString,
                ReleaseInventoryReservationV1.MessageType,
                cancellationToken));
    }

    [Fact]
    public async Task ReleasedOutcomeCompletesCompensationAndFailsOrder()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                27,
                18,
                30,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-release-completes-compensation",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                acceptedAtUtc.AddSeconds(10));

        var reservationId =
            Guid.NewGuid();

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                setup.ReserveCommands[0],
                reservationId,
                acceptedAtUtc.AddSeconds(10)),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateRejectedEnvelope(
                setup.ReserveCommands[1],
                acceptedAtUtc.AddSeconds(11)),
            cancellationToken);

        var releaseMessage =
            Assert.Single(
                await ReadOutboxByTypeAsync(
                    connectionString,
                    ReleaseInventoryReservationV1.MessageType,
                    cancellationToken));

        var release =
            JsonSerializer.Deserialize<ReleaseInventoryReservationV1>(
                releaseMessage.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(
            release);

        var releasedEnvelope =
            CreateReleasedEnvelope(
                release,
                releaseMessage.MessageId,
                acceptedAtUtc.AddSeconds(12));

        await consumer.ConsumeAsync(
            releasedEnvelope,
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateReleasedEnvelope(
                release,
                releaseMessage.MessageId,
                acceptedAtUtc.AddSeconds(12)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.Failed,
            process.State);

        var releasedLine =
            Assert.Single(
                process.Lines,
                line =>
                    line.ReservationRequestId ==
                    release.RequestId);

        Assert.Equal(
            OrderPlacementLineState.Released,
            releasedLine.State);

        Assert.Equal(
            reservationId,
            releasedLine.ReservationId);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderStatus.Failed,
            order.Status);
    }

    [Fact]
    public async Task ExpiredOutcomeCompletesCompensationAndFailsOrder()
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

        var acceptedAtUtc =
            new DateTimeOffset(
                2026,
                9,
                27,
                18,
                40,
                0,
                TimeSpan.Zero);

        var setup =
            await CreateTwoLineOrderAsync(
                connectionString,
                acceptedAtUtc,
                "inventory-expiry-completes-compensation",
                cancellationToken);

        var consumer =
            CreateConsumer(
                connectionString,
                acceptedAtUtc.AddSeconds(10));

        var reservationId =
            Guid.NewGuid();

        await consumer.ConsumeAsync(
            CreateReservedEnvelope(
                setup.ReserveCommands[0],
                reservationId,
                acceptedAtUtc.AddSeconds(10)),
            cancellationToken);

        await consumer.ConsumeAsync(
            CreateRejectedEnvelope(
                setup.ReserveCommands[1],
                acceptedAtUtc.AddSeconds(11)),
            cancellationToken);

        var releaseMessage =
            Assert.Single(
                await ReadOutboxByTypeAsync(
                    connectionString,
                    ReleaseInventoryReservationV1.MessageType,
                    cancellationToken));

        var release =
            JsonSerializer.Deserialize<ReleaseInventoryReservationV1>(
                releaseMessage.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(
            release);

        await consumer.ConsumeAsync(
            CreateExpiredEnvelope(
                release,
                releaseMessage.MessageId,
                acceptedAtUtc.AddSeconds(12)),
            cancellationToken);

        var process =
            await ReadProcessAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderPlacementState.Failed,
            process.State);

        var expiredLine =
            Assert.Single(
                process.Lines,
                line =>
                    line.ReservationRequestId ==
                    release.RequestId);

        Assert.Equal(
            OrderPlacementLineState.Expired,
            expiredLine.State);

        Assert.Equal(
            reservationId,
            expiredLine.ReservationId);

        var order =
            await ReadOrderAsync(
                connectionString,
                setup.OrderId,
                cancellationToken);

        Assert.Equal(
            OrderStatus.Failed,
            order.Status);
    }

    private static OrderingInboundMessageConsumer CreateConsumer(
        string connectionString,
        DateTimeOffset now)
    {
        var timeProvider =
            new FixedTimeProvider(now);

        return new OrderingInboundMessageConsumer(
            new TestOrderingDbContextFactory(
                connectionString),
            timeProvider,
            new IOrderingInboundMessageRoute[]
            {
                new InventoryReservedInboundMessageRoute(
                    timeProvider),
                new InventoryRejectedInboundMessageRoute(
                    timeProvider),
                new InventoryReleasedInboundMessageRoute(
                    timeProvider),
                new InventoryExpiredInboundMessageRoute(
                    timeProvider)
            });
    }

    private static IntegrationMessageEnvelope CreateReleasedEnvelope(
        ReleaseInventoryReservationV1 release,
        Guid causationId,
        DateTimeOffset occurredAtUtc)
    {
        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            InventoryReleasedV1.MessageType,
            JsonSerializer.Serialize(
                new InventoryReleasedV1(
                    release.RequestId,
                    release.OrderId,
                    release.ReservationId,
                    release.Reason,
                    occurredAtUtc),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            release.OrderId,
            causationId);
    }

    private static IntegrationMessageEnvelope CreateExpiredEnvelope(
        ReleaseInventoryReservationV1 release,
        Guid causationId,
        DateTimeOffset occurredAtUtc)
    {
        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            InventoryExpiredV1.MessageType,
            JsonSerializer.Serialize(
                new InventoryExpiredV1(
                    release.RequestId,
                    release.OrderId,
                    release.ReservationId,
                    occurredAtUtc),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            release.OrderId,
            causationId);
    }

    private static IntegrationMessageEnvelope CreateReservedEnvelope(
        ReserveCommandRow reserve,
        Guid reservationId,
        DateTimeOffset occurredAtUtc,
        Guid? messageId = null)
    {
        var eventMessageId =
            messageId ??
            reserve.EventMessageId;

        return new IntegrationMessageEnvelope(
            eventMessageId,
            InventoryReservedV1.MessageType,
            JsonSerializer.Serialize(
                new InventoryReservedV1(
                    reserve.Command.RequestId,
                    reserve.Command.OrderId,
                    reserve.Command.OrderLineId,
                    reserve.Command.SkuCode,
                    reserve.Command.Quantity,
                    reservationId,
                    occurredAtUtc,
                    occurredAtUtc.AddMinutes(15)),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            reserve.Command.OrderId,
            reserve.ReserveMessageId);
    }

    private static IntegrationMessageEnvelope CreateRejectedEnvelope(
        ReserveCommandRow reserve,
        DateTimeOffset occurredAtUtc)
    {
        return new IntegrationMessageEnvelope(
            reserve.EventMessageId,
            InventoryRejectedV1.MessageType,
            JsonSerializer.Serialize(
                new InventoryRejectedV1(
                    reserve.Command.RequestId,
                    reserve.Command.OrderId,
                    reserve.Command.OrderLineId,
                    reserve.Command.SkuCode,
                    reserve.Command.Quantity,
                    InventoryRejectedV1.InsufficientStockReason,
                    occurredAtUtc),
                JsonSerializerOptions.Web),
            occurredAtUtc,
            reserve.Command.OrderId,
            reserve.ReserveMessageId);
    }

    private static async Task<OrderSetup> CreateTwoLineOrderAsync(
        string connectionString,
        DateTimeOffset now,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

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

        var reserves =
            await ReadReserveCommandsAsync(
                connectionString,
                result.OrderId,
                cancellationToken);

        Assert.Equal(
            2,
            reserves.Count);

        return new OrderSetup(
            result.OrderId,
            reserves);
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
                ORDER BY message_id;
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
            var reserveMessageId =
                reader.GetGuid(0);

            var payload =
                JsonSerializer.Deserialize<ReserveInventoryV1>(
                    reader.GetString(1),
                    JsonSerializerOptions.Web);

            Assert.NotNull(payload);

            rows.Add(
                new ReserveCommandRow(
                    reserveMessageId,
                    Guid.NewGuid(),
                    payload));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<OutboxRow>> ReadOutboxByTypeAsync(
        string connectionString,
        string messageType,
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
                    message_id,
                    payload::text,
                    correlation_id,
                    causation_id
                FROM ordering.outbox_messages
                WHERE message_type = @message_type
                ORDER BY occurred_at_utc, message_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "message_type",
            messageType);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new OutboxRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetGuid(3)));
        }

        return rows;
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
                    "switchyard_ordering_inventory_outcomes_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        return postgres;
    }

    private sealed record OrderSetup(
        Guid OrderId,
        IReadOnlyList<ReserveCommandRow> ReserveCommands);

    private sealed record ReserveCommandRow(
        Guid ReserveMessageId,
        Guid EventMessageId,
        ReserveInventoryV1 Command);

    private sealed record OutboxRow(
        Guid MessageId,
        string PayloadJson,
        Guid CorrelationId,
        Guid? CausationId);

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
            OrderPlacementInventoryOutcomeTests.CreateDbContext(
                _connectionString);

        public Task<OrderingDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class CoordinatedRoute :
        IOrderingInboundMessageRoute
    {
        private readonly IOrderingInboundMessageRoute _inner;
        private readonly int _expectedCalls;
        private readonly TaskCompletionSource<bool> _ready =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public CoordinatedRoute(
            IOrderingInboundMessageRoute inner,
            int expectedCalls)
        {
            _inner =
                inner ??
                throw new ArgumentNullException(nameof(inner));

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                expectedCalls);

            _expectedCalls =
                expectedCalls;
        }

        public string MessageType =>
            _inner.MessageType;

        public async Task HandleAsync(
            OrderingDbContext dbContext,
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) ==
                _expectedCalls)
            {
                _ready.TrySetResult(true);
            }

            await _ready.Task.WaitAsync(
                cancellationToken);

            await _inner.HandleAsync(
                dbContext,
                message,
                cancellationToken);
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
