using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Messaging;
using Switchyard.Inventory.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class InventoryInboxTests
{
    private const string ConsumerName = "inventory-reservation-test";

    [Fact]
    public async Task ProcessesMessageOnceAndReplaysWithoutRepeatingOutboxSideEffect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 21, 0, 0, TimeSpan.Zero);
        var incoming = CreateIncomingMessage(now);
        var handlerInvocations = 0;

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var processor = new EfInventoryInboxMessageProcessor(
            dbContext,
            new FixedTimeProvider(now));
        var outbox = new EfInventoryOutboxStore(dbContext);

        async Task HandleAsync(CancellationToken token)
        {
            handlerInvocations++;

            await outbox.AddAsync(
                CreateOutgoingMessage(incoming, now),
                token);
        }

        var first = await processor.ProcessAsync(
            ConsumerName,
            incoming,
            HandleAsync,
            cancellationToken);

        var replay = await processor.ProcessAsync(
            ConsumerName,
            incoming,
            HandleAsync,
            cancellationToken);

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(1, handlerInvocations);
        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.inbox_messages",
            cancellationToken));
        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.outbox_messages",
            cancellationToken));
    }

    [Fact]
    public async Task HandlerFailureRollsBackInboxAndOutboxBeforeRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 21, 5, 0, TimeSpan.Zero);
        var incoming = CreateIncomingMessage(now);

        await using (var failedContext = CreateDbContext(postgres.GetConnectionString()))
        {
            var processor = new EfInventoryInboxMessageProcessor(
                failedContext,
                new FixedTimeProvider(now));
            var outbox = new EfInventoryOutboxStore(failedContext);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => processor.ProcessAsync(
                    ConsumerName,
                    incoming,
                    async token =>
                    {
                        await outbox.AddAsync(
                            CreateOutgoingMessage(incoming, now),
                            token);

                        throw new InvalidOperationException("Simulated handler failure.");
                    },
                    cancellationToken));
        }

        Assert.Equal(0, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.inbox_messages",
            cancellationToken));
        Assert.Equal(0, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.outbox_messages",
            cancellationToken));

        await using var retryContext = CreateDbContext(postgres.GetConnectionString());
        var retryProcessor = new EfInventoryInboxMessageProcessor(
            retryContext,
            new FixedTimeProvider(now.AddSeconds(1)));
        var retryOutbox = new EfInventoryOutboxStore(retryContext);

        var result = await retryProcessor.ProcessAsync(
            ConsumerName,
            incoming,
            token => retryOutbox.AddAsync(
                CreateOutgoingMessage(incoming, now.AddSeconds(1)),
                token),
            cancellationToken);

        Assert.False(result.Replayed);
        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.inbox_messages",
            cancellationToken));
        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.outbox_messages",
            cancellationToken));
    }

    [Fact]
    public async Task ConcurrentDuplicateDeliveriesInvokeHandlerOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 21, 10, 0, TimeSpan.Zero);
        var incoming = CreateIncomingMessage(now);
        var handlerInvocations = 0;

        await using var firstContext = CreateDbContext(postgres.GetConnectionString());
        await using var secondContext = CreateDbContext(postgres.GetConnectionString());

        var firstProcessor = new EfInventoryInboxMessageProcessor(
            firstContext,
            new FixedTimeProvider(now));
        var secondProcessor = new EfInventoryInboxMessageProcessor(
            secondContext,
            new FixedTimeProvider(now));

        async Task HandleAsync(CancellationToken token)
        {
            Interlocked.Increment(ref handlerInvocations);
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
        }

        var firstTask = firstProcessor.ProcessAsync(
            ConsumerName,
            incoming,
            HandleAsync,
            cancellationToken);
        var secondTask = secondProcessor.ProcessAsync(
            ConsumerName,
            incoming,
            HandleAsync,
            cancellationToken);

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, handlerInvocations);
        Assert.Single(results, result => !result.Replayed);
        Assert.Single(results, result => result.Replayed);
        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(),
            "inventory.inbox_messages",
            cancellationToken));
    }

    [Fact]
    public async Task ReusingMessageIdWithDifferentPayloadIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var now = new DateTimeOffset(2026, 9, 19, 21, 15, 0, TimeSpan.Zero);
        var incoming = CreateIncomingMessage(now);

        await using var dbContext = CreateDbContext(postgres.GetConnectionString());
        var processor = new EfInventoryInboxMessageProcessor(
            dbContext,
            new FixedTimeProvider(now));

        var first = await processor.ProcessAsync(
            ConsumerName,
            incoming,
            _ => Task.CompletedTask,
            cancellationToken);

        Assert.False(first.Replayed);

        var conflicting = new IntegrationMessageEnvelope(
            incoming.MessageId,
            incoming.MessageType,
            """{"reservationId":"different"}""",
            incoming.OccurredAtUtc,
            incoming.CorrelationId,
            incoming.CausationId);

        await Assert.ThrowsAsync<InboxMessageConflictException>(
            () => processor.ProcessAsync(
                ConsumerName,
                conflicting,
                _ => Task.CompletedTask,
                cancellationToken));
    }

    private static IntegrationMessageEnvelope CreateIncomingMessage(
        DateTimeOffset occurredAtUtc)
    {
        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            "inventory.command.test-reserve.v1",
            """{"reservationId":"RES-001"}""",
            occurredAtUtc,
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    private static IntegrationMessageEnvelope CreateOutgoingMessage(
        IntegrationMessageEnvelope incoming,
        DateTimeOffset occurredAtUtc)
    {
        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            "inventory.event.test-forwarded.v1",
            """{"accepted":true}""",
            occurredAtUtc,
            incoming.CorrelationId,
            incoming.MessageId);
    }

    private static InventoryDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_inventory_inbox_test")
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
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {tableName};",
            connection);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
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
