using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OrderingOutboxTests
{
    [Fact]
    public async Task OrderAcceptanceAndOutboxMessageCommitTogetherWithoutReplayDuplication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 20, 30, 0, TimeSpan.Zero);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var handler = CreateOrderHandler(dbContext, now);
        var command = CreateOrderCommand("checkout-outbox-001");

        var created = await handler.HandleAsync(command, cancellationToken);
        var replayed = await handler.HandleAsync(command, cancellationToken);

        Assert.False(created.Replayed);
        Assert.True(replayed.Replayed);
        Assert.Equal(created.OrderId, replayed.OrderId);

        var rows = await ReadOutboxRowsAsync(
            postgres.GetConnectionString(),
            cancellationToken);

        Assert.Equal(2, rows.Count);

        var accepted = Assert.Single(
            rows,
            row =>
                row.MessageType ==
                OrderAcceptedIntegrationMessageV1.MessageType);
        var reserve = Assert.Single(
            rows,
            row =>
                row.MessageType ==
                ReserveInventoryV1.MessageType);

        Assert.Equal(created.OrderId, accepted.CorrelationId);
        Assert.Null(accepted.CausationId);
        Assert.Equal(now, accepted.OccurredAtUtc);
        Assert.Equal(now, accepted.AvailableAtUtc);
        Assert.Equal(0, accepted.DeliveryAttemptCount);
        Assert.Null(accepted.PublishedAtUtc);

        var payload = JsonSerializer.Deserialize<OrderAcceptedIntegrationMessageV1>(
            accepted.PayloadJson,
            JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(created.OrderId, payload.OrderId);
        Assert.Equal(created.OrderNumber, payload.OrderNumber);
        Assert.Equal(created.TotalAmount, payload.TotalAmount);
        Assert.Equal(created.Currency, payload.Currency);

        Assert.Equal(created.OrderId, reserve.CorrelationId);
        Assert.Equal(accepted.MessageId, reserve.CausationId);
        Assert.Equal(now, reserve.OccurredAtUtc);
        Assert.Equal(now, reserve.AvailableAtUtc);
        Assert.Equal(0, reserve.DeliveryAttemptCount);
        Assert.Null(reserve.PublishedAtUtc);
    }

    [Fact]
    public async Task ExpiredLeaseMakesMessageClaimableAgainForAtLeastOnceDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 20, 35, 0, TimeSpan.Zero);
        var messageId = await SeedOrderOutboxMessageAsync(
            postgres.GetConnectionString(),
            now,
            cancellationToken);

        await using var firstContext = CreateDbContext(postgres.GetConnectionString());
        await using var secondContext = CreateDbContext(postgres.GetConnectionString());

        var firstStore = new EfOrderingOutboxStore(firstContext);
        var secondStore = new EfOrderingOutboxStore(secondContext);

        var firstClaim = Assert.Single(
            await firstStore.ClaimPendingAsync(
                10,
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        Assert.Equal(messageId, firstClaim.Message.MessageId);
        Assert.Equal(1, firstClaim.DeliveryAttemptCount);

        Assert.Empty(
            await secondStore.ClaimPendingAsync(
                10,
                now.AddSeconds(30),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        var secondClaim = Assert.Single(
            await secondStore.ClaimPendingAsync(
                10,
                now.AddMinutes(2),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        Assert.Equal(messageId, secondClaim.Message.MessageId);
        Assert.NotEqual(firstClaim.LockToken, secondClaim.LockToken);
        Assert.Equal(2, secondClaim.DeliveryAttemptCount);

        await Assert.ThrowsAsync<OutboxLeaseLostException>(
            () => firstStore.MarkPublishedAsync(
                messageId,
                firstClaim.LockToken,
                now.AddMinutes(2),
                cancellationToken));

        await secondStore.MarkPublishedAsync(
            messageId,
            secondClaim.LockToken,
            now.AddMinutes(2),
            cancellationToken);

        var row = Assert.Single(
            await ReadOutboxRowsAsync(
                postgres.GetConnectionString(),
                cancellationToken));

        Assert.Equal(2, row.DeliveryAttemptCount);
        Assert.Equal(now.AddMinutes(2), row.PublishedAtUtc);
    }

    [Fact]
    public async Task ExpiredLeaseCannotBeAcknowledgedBeforeAnotherWorkerReclaimsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 20, 38, 0, TimeSpan.Zero);
        var messageId = await SeedOrderOutboxMessageAsync(
            postgres.GetConnectionString(),
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var store = new EfOrderingOutboxStore(dbContext);

        var claimed = Assert.Single(
            await store.ClaimPendingAsync(
                10,
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        await Assert.ThrowsAsync<OutboxLeaseLostException>(
            () => store.MarkPublishedAsync(
                messageId,
                claimed.LockToken,
                now.AddMinutes(2),
                cancellationToken));

        var row = Assert.Single(
            await ReadOutboxRowsAsync(
                postgres.GetConnectionString(),
                cancellationToken));

        Assert.Equal(1, row.DeliveryAttemptCount);
        Assert.Null(row.PublishedAtUtc);
    }
    [Fact]
    public async Task ConcurrentClaimersDoNotOwnTheSameMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 20, 40, 0, TimeSpan.Zero);
        await SeedOrderOutboxMessageAsync(
            postgres.GetConnectionString(),
            now,
            cancellationToken);

        await using var firstContext = CreateDbContext(postgres.GetConnectionString());
        await using var secondContext = CreateDbContext(postgres.GetConnectionString());

        var firstStore = new EfOrderingOutboxStore(firstContext);
        var secondStore = new EfOrderingOutboxStore(secondContext);

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var firstTask = Task.Run(
            () => ClaimAfterBarrierAsync(
                firstStore,
                now.AddSeconds(1),
                ready,
                start,
                cancellationToken),
            cancellationToken);

        var secondTask = Task.Run(
            () => ClaimAfterBarrierAsync(
                secondStore,
                now.AddSeconds(1),
                ready,
                start,
                cancellationToken),
            cancellationToken);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        start.Set();

        var claims = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, claims.Sum(claim => claim.Count));
        Assert.Equal(1, claims.Count(claim => claim.Count == 1));
        Assert.Equal(1, claims.Count(claim => claim.Count == 0));
    }

    [Fact]
    public async Task FailedDeliveryReleasesLeaseAndSchedulesNextAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 20, 45, 0, TimeSpan.Zero);
        var messageId = await SeedOrderOutboxMessageAsync(
            postgres.GetConnectionString(),
            now,
            cancellationToken);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var store = new EfOrderingOutboxStore(dbContext);

        var claimed = Assert.Single(
            await store.ClaimPendingAsync(
                10,
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        await store.MarkFailedAsync(
            messageId,
            claimed.LockToken,
            now.AddSeconds(2),
            now.AddMinutes(1),
            "HttpRequestException",
            cancellationToken);

        Assert.Empty(
            await store.ClaimPendingAsync(
                10,
                now.AddSeconds(30),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        var retried = Assert.Single(
            await store.ClaimPendingAsync(
                10,
                now.AddMinutes(1),
                TimeSpan.FromMinutes(1),
                cancellationToken));

        Assert.Equal(2, retried.DeliveryAttemptCount);
    }

    private static async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAfterBarrierAsync(
        EfOrderingOutboxStore store,
        DateTimeOffset now,
        CountdownEvent ready,
        ManualResetEventSlim start,
        CancellationToken cancellationToken)
    {
        ready.Signal();
        start.Wait(cancellationToken);

        return await store.ClaimPendingAsync(
            1,
            now,
            TimeSpan.FromMinutes(1),
            cancellationToken);
    }

    private static async Task<Guid> SeedOrderOutboxMessageAsync(
        string connectionString,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        var store = new EfOrderingOutboxStore(dbContext);
        var messageId = Guid.NewGuid();

        await store.AddAsync(
            new IntegrationMessageEnvelope(
                messageId,
                "ordering.test-outbox.v1",
                """{"test":true}""",
                now,
                Guid.NewGuid(),
                causationId: null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return messageId;
    }

    private static CreatePendingOrderHandler CreateOrderHandler(
        OrderingDbContext dbContext,
        DateTimeOffset now)
    {
        return new CreatePendingOrderHandler(
            new EfOrderRepository(dbContext),
            new EfOrderRequestRepository(dbContext),
            new EfOrderPlacementProcessRepository(dbContext),
            new EfOrderingUnitOfWork(dbContext),
            new EfOrderingOutboxStore(dbContext),
            new PostgresOrderNumberGenerator(dbContext),
            new FixedTimeProvider(now));
    }

    private static CreatePendingOrderCommand CreateOrderCommand(string idempotencyKey)
    {
        return new CreatePendingOrderCommand(
            idempotencyKey,
            new[]
            {
                new CreatePendingOrderLine(
                    "BIKE-001",
                    "Road Bike",
                    1,
                    1299.99m,
                    "GBP")
            });
    }

    private static OrderingDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderingDbContext(options);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_ordering_outbox_test")
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

    private static async Task<IReadOnlyList<OutboxRow>> ReadOutboxRowsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var rows = new List<OutboxRow>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                message_id,
                message_type,
                payload::text,
                occurred_at_utc,
                correlation_id,
                causation_id,
                available_at_utc,
                delivery_attempt_count,
                published_at_utc
            FROM ordering.outbox_messages
            ORDER BY occurred_at_utc, message_id;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new OutboxRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return rows;
    }

    private sealed record OutboxRow(
        Guid MessageId,
        string MessageType,
        string PayloadJson,
        DateTimeOffset OccurredAtUtc,
        Guid CorrelationId,
        Guid? CausationId,
        DateTimeOffset AvailableAtUtc,
        int DeliveryAttemptCount,
        DateTimeOffset? PublishedAtUtc);

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
