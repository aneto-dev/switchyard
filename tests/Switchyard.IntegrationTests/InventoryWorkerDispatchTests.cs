using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Messaging;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class InventoryWorkerDispatchTests
{
    [Fact]
    public async Task DispatchCyclePublishesAndMarksInventoryOutboxMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_inventory_worker_dispatch_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();
        await postgres.StartAsync(cancellationToken);

        var connectionString = postgres.GetConnectionString();
        await using (var migrationContext = CreateDbContext(connectionString))
        {
            await migrationContext.Database.MigrateAsync(cancellationToken);
        }

        var acceptedAtUtc = new DateTimeOffset(2026, 9, 20, 14, 10, 0, TimeSpan.Zero);
        await SeedOutboxAsync(connectionString, acceptedAtUtc, cancellationToken);

        var transport = new RecordingTransport();
        var dispatchAtUtc = acceptedAtUtc.AddSeconds(1);
        var cycle = new InventoryOutboxDispatchCycle(
            new TestInventoryDbContextFactory(connectionString),
            transport,
            new FixedTimeProvider(dispatchAtUtc),
            new InventoryOutboxWorkerOptions(
                10, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500)));

        var result = await cycle.DispatchAsync(cancellationToken);

        Assert.Equal(new OutboxDispatchResult(1, 1, 0), result);
        Assert.Single(transport.Published);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT published_at_utc FROM inventory.outbox_messages ORDER BY occurred_at_utc, message_id LIMIT 1;",
            connection);
        var publishedAtUtc = await command.ExecuteScalarAsync(cancellationToken);
        Assert.Equal(dispatchAtUtc.UtcDateTime, Assert.IsType<DateTime>(publishedAtUtc));
    }

    private static async Task SeedOutboxAsync(
        string connectionString, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        var store = new EfInventoryOutboxStore(dbContext);

        await store.AddAsync(
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                "inventory.event.test-worker-dispatch.v1",
                """{"test":true}""",
                now,
                Guid.NewGuid(),
                causationId: null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InventoryDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new InventoryDbContext(options);
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        public List<IntegrationMessageEnvelope> Published { get; } = [];
        public Task PublishAsync(IntegrationMessageEnvelope message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestInventoryDbContextFactory : IDbContextFactory<InventoryDbContext>
    {
        private readonly string _connectionString;
        public TestInventoryDbContextFactory(string connectionString) => _connectionString = connectionString;
        public InventoryDbContext CreateDbContext() => InventoryWorkerDispatchTests.CreateDbContext(_connectionString);
        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
