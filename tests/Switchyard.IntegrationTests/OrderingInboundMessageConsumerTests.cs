using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Messaging;
using Switchyard.Ordering.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OrderingInboundMessageConsumerTests
{
    [Fact]
    public async Task DuplicateDeliveryUsesDurableInboxAndRunsRouteOnce()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_ordering_receiver_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await using (var migrationContext =
            CreateDbContext(connectionString))
        {
            await migrationContext.Database.MigrateAsync(
                cancellationToken);
        }

        var now =
            new DateTimeOffset(
                2026,
                9,
                21,
                19,
                0,
                0,
                TimeSpan.Zero);

        var route =
            new RecordingRoute(now);

        var consumer =
            new OrderingInboundMessageConsumer(
                new TestOrderingDbContextFactory(
                    connectionString),
                new FixedTimeProvider(now),
                new[] { route });

        var incoming =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                route.MessageType,
                """{"reservationId":"RES-001"}""",
                now,
                Guid.NewGuid(),
                Guid.NewGuid());

        await consumer.ConsumeAsync(
            incoming,
            cancellationToken);

        await consumer.ConsumeAsync(
            incoming,
            cancellationToken);

        Assert.Equal(1, route.Invocations);

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "ordering.inbox_messages",
                cancellationToken));

        Assert.Equal(
            1,
            await CountRowsAsync(
                connectionString,
                "ordering.outbox_messages",
                cancellationToken));
    }

    private static OrderingDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<OrderingDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new OrderingDbContext(options);
    }

    private static async Task<int> CountRowsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(
                $"SELECT count(*) FROM {tableName};",
                connection);

        var value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class RecordingRoute :
        IOrderingInboundMessageRoute
    {
        private readonly DateTimeOffset _now;

        public RecordingRoute(DateTimeOffset now)
        {
            _now = now;
        }

        public string MessageType =>
            "inventory.reservation-confirmed.v1";

        public int Invocations { get; private set; }

        public Task HandleAsync(
            OrderingDbContext dbContext,
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken)
        {
            Invocations++;

            var outbox =
                new EfOrderingOutboxStore(dbContext);

            return outbox.AddAsync(
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    "ordering.test-forwarded.v1",
                    """{"accepted":true}""",
                    _now,
                    message.CorrelationId,
                    message.MessageId),
                cancellationToken);
        }
    }

    private sealed class TestOrderingDbContextFactory :
        IDbContextFactory<OrderingDbContext>
    {
        private readonly string _connectionString;

        public TestOrderingDbContextFactory(
            string connectionString)
        {
            _connectionString = connectionString;
        }

        public OrderingDbContext CreateDbContext() =>
            OrderingInboundMessageConsumerTests.CreateDbContext(
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

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;
    }
}
