using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Application.Shipments;
using Switchyard.Fulfilment.Application.Units;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.IntegrationContracts.Fulfilment;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class FulfilmentShipmentProgressionTests
{
    [Fact]
    public async Task ShipmentProgressionPublishesCreatedDispatchedAndDeliveredFactsOnce()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_fulfilment_progression_test")
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
        var paymentId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var firstUnitId = Guid.NewGuid();
        var secondUnitId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        var confirmedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            await new CreateFulfilmentHandler(
                new EfFulfilmentRequestRepository(
                    dbContext)).HandleAsync(
                new CreateFulfilmentCommand(
                    orderId,
                    paymentId,
                    "SW-SHIP-002",
                    new[]
                    {
                        new CreateFulfilmentLine(
                            lineId,
                            "BIKE-002",
                            "Gravel Bike",
                            2)
                    },
                    confirmedAtUtc),
                cancellationToken);

            await dbContext.SaveChangesAsync(
                cancellationToken);
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var units =
                new CreateFulfilmentUnitHandler(
                    new EfFulfilmentRequestRepository(
                        dbContext));

            await units.HandleAsync(
                new CreateFulfilmentUnitCommand(
                    orderId,
                    firstUnitId,
                    new[]
                    {
                        new CreateFulfilmentUnitLine(
                            lineId,
                            1)
                    },
                    confirmedAtUtc.AddMinutes(1)),
                cancellationToken);

            await units.HandleAsync(
                new CreateFulfilmentUnitCommand(
                    orderId,
                    secondUnitId,
                    new[]
                    {
                        new CreateFulfilmentUnitLine(
                            lineId,
                            1)
                    },
                    confirmedAtUtc.AddMinutes(2)),
                cancellationToken);
        }

        var createdAtUtc =
            confirmedAtUtc.AddMinutes(3);
        var dispatchedAtUtc =
            confirmedAtUtc.AddMinutes(4);
        var deliveredAtUtc =
            confirmedAtUtc.AddMinutes(10);

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var handler =
                new CreateShipmentHandler(
                    new EfFulfilmentRequestRepository(
                        dbContext),
                    new EfFulfilmentOutboxStore(
                        dbContext));

            var first =
                await handler.HandleAsync(
                    new CreateShipmentCommand(
                        orderId,
                        shipmentId,
                        new[]
                        {
                            firstUnitId,
                            secondUnitId
                        },
                        Guid.NewGuid(),
                        createdAtUtc),
                    cancellationToken);

            var replay =
                await handler.HandleAsync(
                    new CreateShipmentCommand(
                        orderId,
                        shipmentId,
                        new[]
                        {
                            firstUnitId,
                            secondUnitId
                        },
                        Guid.NewGuid(),
                        createdAtUtc),
                    cancellationToken);

            Assert.False(first.Replayed);
            Assert.True(replay.Replayed);
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var handler =
                new DispatchShipmentHandler(
                    new EfFulfilmentRequestRepository(
                        dbContext),
                    new EfFulfilmentOutboxStore(
                        dbContext));

            var first =
                await handler.HandleAsync(
                    new DispatchShipmentCommand(
                        orderId,
                        shipmentId,
                        Guid.NewGuid(),
                        dispatchedAtUtc),
                    cancellationToken);

            var replay =
                await handler.HandleAsync(
                    new DispatchShipmentCommand(
                        orderId,
                        shipmentId,
                        Guid.NewGuid(),
                        dispatchedAtUtc),
                    cancellationToken);

            Assert.False(first.Replayed);
            Assert.True(replay.Replayed);
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var handler =
                new DeliverShipmentHandler(
                    new EfFulfilmentRequestRepository(
                        dbContext),
                    new EfFulfilmentOutboxStore(
                        dbContext));

            var first =
                await handler.HandleAsync(
                    new DeliverShipmentCommand(
                        orderId,
                        shipmentId,
                        Guid.NewGuid(),
                        deliveredAtUtc),
                    cancellationToken);

            var replay =
                await handler.HandleAsync(
                    new DeliverShipmentCommand(
                        orderId,
                        shipmentId,
                        Guid.NewGuid(),
                        deliveredAtUtc),
                    cancellationToken);

            Assert.False(first.Replayed);
            Assert.True(replay.Replayed);
        }

        await using var connection =
            new NpgsqlConnection(
                connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                SELECT message_type, payload::text
                FROM fulfilment.outbox_messages
                WHERE correlation_id = @order_id
                  AND message_type = ANY(@message_types)
                ORDER BY occurred_at_utc;
                """,
                connection);

        command.Parameters.AddWithValue(
            "order_id",
            orderId);

        command.Parameters.AddWithValue(
            "message_types",
            new[]
            {
                ShipmentCreatedV1.MessageType,
                ShipmentDispatchedV1.MessageType,
                ShipmentDeliveredV1.MessageType
            });

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var events =
            new List<(string Type, string Payload)>();

        while (await reader.ReadAsync(
            cancellationToken))
        {
            events.Add(
                (
                    reader.GetString(0),
                    reader.GetString(1)
                ));
        }

        Assert.Equal(3, events.Count);
        Assert.Equal(
            ShipmentCreatedV1.MessageType,
            events[0].Type);
        Assert.Equal(
            ShipmentDispatchedV1.MessageType,
            events[1].Type);
        Assert.Equal(
            ShipmentDeliveredV1.MessageType,
            events[2].Type);

        var delivered =
            JsonSerializer.Deserialize<ShipmentDeliveredV1>(
                events[2].Payload,
                JsonSerializerOptions.Web);

        Assert.NotNull(delivered);
        Assert.Equal(orderId, delivered.OrderId);
        Assert.Equal(shipmentId, delivered.ShipmentId);
        Assert.Single(delivered.Lines);
        Assert.Equal(lineId, delivered.Lines[0].OrderLineId);
        Assert.Equal(2, delivered.Lines[0].Quantity);
        Assert.Equal(deliveredAtUtc, delivered.DeliveredAtUtc);
    }

    private static FulfilmentDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<FulfilmentDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new FulfilmentDbContext(options);
    }
}
