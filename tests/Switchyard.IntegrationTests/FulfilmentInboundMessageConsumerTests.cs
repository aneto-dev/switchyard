using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.IntegrationContracts.Ordering;
using Switchyard.Messaging;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class FulfilmentInboundMessageConsumerTests
{
    [Fact]
    public async Task ConfirmedOrderCreatesFulfilmentRequestIdempotently()
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

        var confirmedAtUtc =
            new DateTimeOffset(
                2026,
                11,
                3,
                9,
                15,
                0,
                TimeSpan.Zero);

        var orderId =
            Guid.NewGuid();

        var lines =
            new[]
            {
                new OrderConfirmedLineV1(
                    Guid.NewGuid(),
                    "BIKE-001",
                    "Road Bike",
                    1),
                new OrderConfirmedLineV1(
                    Guid.NewGuid(),
                    "HELMET-001",
                    "Helmet",
                    2)
            };

        var confirmed =
            new OrderConfirmedV1(
                orderId,
                "SW-000001",
                1499.97m,
                "GBP",
                confirmedAtUtc,
                lines);

        var envelope =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                OrderConfirmedV1.MessageType,
                JsonSerializer.Serialize(
                    confirmed,
                    JsonSerializerOptions.Web),
                confirmedAtUtc,
                orderId,
                Guid.NewGuid());

        var consumer =
            CreateConsumer(
                connectionString,
                confirmedAtUtc.AddSeconds(2));

        await consumer.ConsumeAsync(
            envelope,
            cancellationToken);

        await consumer.ConsumeAsync(
            envelope,
            cancellationToken);

        Assert.Equal(
            1,
            await CountAsync(
                connectionString,
                "fulfilment.requests",
                cancellationToken));

        Assert.Equal(
            2,
            await CountAsync(
                connectionString,
                "fulfilment.request_lines",
                cancellationToken));

        Assert.Equal(
            1,
            await CountAsync(
                connectionString,
                "fulfilment.inbox_messages",
                cancellationToken));

        var persisted =
            await ReadRequestAsync(
                connectionString,
                orderId,
                cancellationToken);

        Assert.Equal(
            "SW-000001",
            persisted.OrderNumber);

        Assert.Equal(
            confirmedAtUtc,
            persisted.ConfirmedAtUtc);

        Assert.Equal(
            2,
            persisted.Lines.Count);

        Assert.Equal(
            "BIKE-001",
            persisted.Lines[0].SkuCode);

        Assert.Equal(
            1,
            persisted.Lines[0].Quantity);

        Assert.Equal(
            "HELMET-001",
            persisted.Lines[1].SkuCode);

        Assert.Equal(
            2,
            persisted.Lines[1].Quantity);
    }

    [Fact]
    public async Task SameMessageIdWithDifferentConfirmedOrderPayloadIsRejected()
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

        var confirmedAtUtc =
            new DateTimeOffset(
                2026,
                11,
                3,
                9,
                20,
                0,
                TimeSpan.Zero);

        var orderId =
            Guid.NewGuid();

        var messageId =
            Guid.NewGuid();

        var orderLineId =
            Guid.NewGuid();

        var first =
            new IntegrationMessageEnvelope(
                messageId,
                OrderConfirmedV1.MessageType,
                JsonSerializer.Serialize(
                    new OrderConfirmedV1(
                        orderId,
                        "SW-000002",
                        1299.99m,
                        "GBP",
                        confirmedAtUtc,
                        new[]
                        {
                            new OrderConfirmedLineV1(
                                orderLineId,
                                "BIKE-001",
                                "Road Bike",
                                1)
                        }),
                    JsonSerializerOptions.Web),
                confirmedAtUtc,
                orderId,
                Guid.NewGuid());

        var consumer =
            CreateConsumer(
                connectionString,
                confirmedAtUtc.AddSeconds(2));

        await consumer.ConsumeAsync(
            first,
            cancellationToken);

        var conflictingPayload =
            JsonSerializer.Serialize(
                new OrderConfirmedV1(
                    orderId,
                    "SW-000002",
                    1299.99m,
                    "GBP",
                    confirmedAtUtc,
                    new[]
                    {
                        new OrderConfirmedLineV1(
                            orderLineId,
                            "BIKE-001",
                            "Road Bike",
                            2)
                    }),
                JsonSerializerOptions.Web);

        var conflicting =
            new IntegrationMessageEnvelope(
                first.MessageId,
                first.MessageType,
                conflictingPayload,
                first.OccurredAtUtc,
                first.CorrelationId,
                first.CausationId);

        await Assert.ThrowsAsync<InboxMessageConflictException>(
            () =>
                consumer.ConsumeAsync(
                    conflicting,
                    cancellationToken));

        Assert.Equal(
            1,
            await CountAsync(
                connectionString,
                "fulfilment.requests",
                cancellationToken));
    }

    private static FulfilmentInboundMessageConsumer CreateConsumer(
        string connectionString,
        DateTimeOffset now)
    {
        return new FulfilmentInboundMessageConsumer(
            new TestFulfilmentDbContextFactory(
                connectionString),
            new FixedTimeProvider(
                now),
            new IFulfilmentInboundMessageRoute[]
            {
                new OrderConfirmedFulfilmentInboundMessageRoute()
            });
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres =
            new PostgreSqlBuilder(
                "postgres:18-alpine")
                .WithDatabase(
                    "switchyard_fulfilment_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        return postgres;
    }

    private static FulfilmentDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<FulfilmentDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        return new FulfilmentDbContext(
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

    private static async Task<int> CountAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

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

    private static async Task<PersistedRequest> ReadRequestAsync(
        string connectionString,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        string orderNumber;
        DateTimeOffset confirmedAtUtc;

        await using (var requestCommand =
            new NpgsqlCommand(
                """
                SELECT
                    order_number,
                    confirmed_at_utc
                FROM fulfilment.requests
                WHERE order_id = @order_id;
                """,
                connection))
        {
            requestCommand.Parameters.AddWithValue(
                "order_id",
                orderId);

            await using var reader =
                await requestCommand.ExecuteReaderAsync(
                    cancellationToken);

            Assert.True(
                await reader.ReadAsync(
                    cancellationToken));

            orderNumber =
                reader.GetString(0);

            confirmedAtUtc =
                reader.GetFieldValue<DateTimeOffset>(
                    1);
        }

        var lines =
            new List<PersistedLine>();

        await using (var lineCommand =
            new NpgsqlCommand(
                """
                SELECT
                    sku_code,
                    quantity
                FROM fulfilment.request_lines
                WHERE order_id = @order_id
                ORDER BY position;
                """,
                connection))
        {
            lineCommand.Parameters.AddWithValue(
                "order_id",
                orderId);

            await using var reader =
                await lineCommand.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                lines.Add(
                    new PersistedLine(
                        reader.GetString(0),
                        reader.GetInt32(1)));
            }
        }

        return new PersistedRequest(
            orderNumber,
            confirmedAtUtc,
            lines);
    }

    private sealed record PersistedRequest(
        string OrderNumber,
        DateTimeOffset ConfirmedAtUtc,
        IReadOnlyList<PersistedLine> Lines);

    private sealed record PersistedLine(
        string SkuCode,
        int Quantity);

    private sealed class TestFulfilmentDbContextFactory :
        IDbContextFactory<FulfilmentDbContext>
    {
        private readonly string _connectionString;

        public TestFulfilmentDbContextFactory(
            string connectionString)
        {
            _connectionString =
                connectionString;
        }

        public FulfilmentDbContext CreateDbContext() =>
            FulfilmentInboundMessageConsumerTests.CreateDbContext(
                _connectionString);

        public Task<FulfilmentDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                CreateDbContext());
        }
    }

    private sealed class FixedTimeProvider :
        TimeProvider
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
