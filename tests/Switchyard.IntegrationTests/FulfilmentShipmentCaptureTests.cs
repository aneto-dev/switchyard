using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Application.Shipments;
using Switchyard.Fulfilment.Application.Units;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.IntegrationContracts.Payments;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class FulfilmentShipmentCaptureTests
{
    [Fact]
    public async Task FirstDispatchStagesOneCaptureCommandAcrossReplay()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("switchyard_fulfilment_shipment_test")
                .WithUsername("switchyard")
                .WithPassword("switchyard-test-only")
                .Build();

        await postgres.StartAsync(cancellationToken);

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
        var unitId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            await new CreateFulfilmentHandler(
                new EfFulfilmentRequestRepository(
                    dbContext)).HandleAsync(
                new CreateFulfilmentCommand(
                    orderId,
                    paymentId,
                    "SW-SHIP-001",
                    new[]
                    {
                        new CreateFulfilmentLine(
                            lineId,
                            "BIKE-001",
                            "Road Bike",
                            1)
                    },
                    new DateTimeOffset(
                        2026,
                        11,
                        3,
                        12,
                        0,
                        0,
                        TimeSpan.Zero)),
                cancellationToken);

            await dbContext.SaveChangesAsync(
                cancellationToken);
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            await new CreateFulfilmentUnitHandler(
                new EfFulfilmentRequestRepository(
                    dbContext)).HandleAsync(
                new CreateFulfilmentUnitCommand(
                    orderId,
                    unitId,
                    new[]
                    {
                        new CreateFulfilmentUnitLine(
                            lineId,
                            1)
                    },
                    new DateTimeOffset(
                        2026,
                        11,
                        3,
                        12,
                        5,
                        0,
                        TimeSpan.Zero)),
                cancellationToken);
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            await new CreateShipmentHandler(
                new EfFulfilmentRequestRepository(
                    dbContext),
                new EfFulfilmentOutboxStore(
                    dbContext)).HandleAsync(
                new CreateShipmentCommand(
                    orderId,
                    shipmentId,
                    new[] { unitId },
                    Guid.NewGuid(),
                    new DateTimeOffset(
                        2026,
                        11,
                        3,
                        12,
                        10,
                        0,
                        TimeSpan.Zero)),
                cancellationToken);
        }

        DispatchShipmentResult first;

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            first =
                await new DispatchShipmentHandler(
                    new EfFulfilmentRequestRepository(
                        dbContext),
                    new EfFulfilmentOutboxStore(
                        dbContext)).HandleAsync(
                    new DispatchShipmentCommand(
                        orderId,
                        shipmentId,
                        Guid.NewGuid(),
                        new DateTimeOffset(
                            2026,
                            11,
                            3,
                            12,
                            15,
                            0,
                            TimeSpan.Zero)),
                    cancellationToken);
        }

        await using (var dbContext =
            CreateDbContext(connectionString))
        {
            var replay =
                await new DispatchShipmentHandler(
                    new EfFulfilmentRequestRepository(
                        dbContext),
                    new EfFulfilmentOutboxStore(
                        dbContext)).HandleAsync(
                    new DispatchShipmentCommand(
                        orderId,
                        shipmentId,
                        Guid.NewGuid(),
                        new DateTimeOffset(
                            2026,
                            11,
                            3,
                            12,
                            15,
                            0,
                            TimeSpan.Zero)),
                    cancellationToken);

            Assert.True(replay.Replayed);
            Assert.False(replay.CaptureRequired);
        }

        Assert.True(first.CaptureRequired);
        Assert.NotNull(first.PaymentCaptureRequestId);

        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var sql =
            new NpgsqlCommand(
                """
                SELECT payload::text
                FROM fulfilment.outbox_messages
                WHERE message_type = @message_type
                  AND correlation_id = @order_id;
                """,
                connection);

        sql.Parameters.AddWithValue(
            "message_type",
            CapturePaymentV1.MessageType);

        sql.Parameters.AddWithValue(
            "order_id",
            orderId);

        var payloadJson =
            Assert.IsType<string>(
                await sql.ExecuteScalarAsync(
                    cancellationToken));

        var payload =
            JsonSerializer.Deserialize<CapturePaymentV1>(
                payloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(
            first.PaymentCaptureRequestId,
            payload.RequestId);
        Assert.Equal(orderId, payload.OrderId);
        Assert.Equal(paymentId, payload.PaymentId);
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
