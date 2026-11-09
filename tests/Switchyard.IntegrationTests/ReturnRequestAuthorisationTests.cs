using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.IntegrationContracts.Fulfilment;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Requests;
using Switchyard.Returns.Domain.Eligibility;
using Switchyard.Returns.Infrastructure.Persistence;
using Switchyard.Worker;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class ReturnRequestAuthorisationTests
{
    [Fact]
    public async Task ConcurrentRequestsCannotAuthoriseMoreThanDeliveredQuantity()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_return_authorisation_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await using (var migrations =
            CreateDbContext(connectionString))
        {
            await migrations.Database.MigrateAsync(
                cancellationToken);
        }

        var orderId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();

        await ProjectDeliveryAsync(
            connectionString,
            orderId,
            orderLineId,
            cancellationToken);

        var requestedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 12, 30, 0, TimeSpan.Zero);

        var attempts =
            await Task.WhenAll(
                TryAuthoriseAsync(
                    connectionString,
                    Guid.NewGuid(),
                    orderId,
                    orderLineId,
                    requestedAtUtc,
                    cancellationToken),
                TryAuthoriseAsync(
                    connectionString,
                    Guid.NewGuid(),
                    orderId,
                    orderLineId,
                    requestedAtUtc,
                    cancellationToken));

        Assert.Single(
            attempts.Where(
                authorised =>
                    authorised));

        Assert.Single(
            attempts.Where(
                authorised =>
                    !authorised));

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT
                    delivered_quantity,
                    authorised_return_quantity,
                    returned_quantity,
                    delivered_quantity - authorised_return_quantity - returned_quantity
                        AS returnable_quantity,
                    (
                        SELECT COUNT(*)
                        FROM returns.return_requests
                        WHERE order_id = @order_id
                    ) AS return_request_count
                FROM returns.return_eligibility_lines
                WHERE order_id = @order_id
                  AND order_line_id = @order_line_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "order_line_id",
            orderLineId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        Assert.Equal(
            1,
            reader.GetInt32(0));

        Assert.Equal(
            1,
            reader.GetInt32(1));

        Assert.Equal(
            0,
            reader.GetInt32(2));

        Assert.Equal(
            0,
            reader.GetInt32(3));

        Assert.Equal(
            1L,
            reader.GetInt64(4));
    }

    private static async Task ProjectDeliveryAsync(
        string connectionString,
        Guid orderId,
        Guid orderLineId,
        CancellationToken cancellationToken)
    {
        var deliveredAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        var payload =
            new ShipmentDeliveredV1(
                orderId,
                Guid.NewGuid(),
                "SW-RET-200",
                new[]
                {
                    new ShipmentLineV1(
                        orderLineId,
                        1)
                },
                deliveredAtUtc);

        var message =
            new IntegrationMessageEnvelope(
                Guid.NewGuid(),
                ShipmentDeliveredV1.MessageType,
                JsonSerializer.Serialize(
                    payload,
                    JsonSerializerOptions.Web),
                deliveredAtUtc,
                orderId,
                Guid.NewGuid());

        var consumer =
            new ReturnsInboundMessageConsumer(
                new TestReturnsDbContextFactory(
                    connectionString),
                TimeProvider.System,
                new IReturnsInboundMessageRoute[]
                {
                    new ShipmentDeliveredReturnsInboundMessageRoute()
                });

        await consumer.ConsumeAsync(
            message,
            cancellationToken);
    }

    private static async Task<bool> TryAuthoriseAsync(
        string connectionString,
        Guid returnRequestId,
        Guid orderId,
        Guid orderLineId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var repository =
            new EfReturnEligibilityRepository(
                dbContext);

        var handler =
            new AuthoriseReturnRequestHandler(
                repository,
                new EfReturnsUnitOfWork(
                    dbContext));

        try
        {
            await handler.HandleAsync(
                new AuthoriseReturnRequestCommand(
                    returnRequestId,
                    orderId,
                    new[]
                    {
                        new AuthoriseReturnRequestLine(
                            orderLineId,
                            1)
                    },
                    requestedAtUtc),
                cancellationToken);

            return true;
        }
        catch (ReturnQuantityNotEligibleException)
        {
            return false;
        }
    }

    private static ReturnsDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<ReturnsDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new ReturnsDbContext(
            options);
    }

    private sealed class TestReturnsDbContextFactory :
        IDbContextFactory<ReturnsDbContext>
    {
        private readonly string _connectionString;

        public TestReturnsDbContextFactory(
            string connectionString)
        {
            _connectionString =
                connectionString;
        }

        public ReturnsDbContext CreateDbContext() =>
            ReturnRequestAuthorisationTests.CreateDbContext(
                _connectionString);

        public Task<ReturnsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ReturnRequestAuthorisationTests.CreateDbContext(
                    _connectionString));
    }
}
