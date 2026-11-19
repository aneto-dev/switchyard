using Microsoft.EntityFrameworkCore;
using Switchyard.Fulfilment.Application.Cancellations;
using Switchyard.Fulfilment.Application.Payments;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Application.Shipments;
using Switchyard.Fulfilment.Application.Units;
using Switchyard.Fulfilment.Domain.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OperationsFulfilmentQueriesTests
{
    private static readonly DateTimeOffset ConfirmedAtUtc = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadsFulfilmentCaptureShipmentsAndCancellationsForOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_operations_fulfilment_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();

        await using (var migrations = CreateDbContext(connectionString))
        {
            await migrations.Database.MigrateAsync(cancellationToken);
        }

        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var bikeLineId = Guid.NewGuid();
        var helmetLineId = Guid.NewGuid();
        var firstUnitId = Guid.NewGuid();
        var secondUnitId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var acceptedCancellationId = Guid.NewGuid();
        var rejectedCancellationId = Guid.NewGuid();

        await using (var dbContext = CreateDbContext(connectionString))
        {
            await new CreateFulfilmentHandler(new EfFulfilmentRequestRepository(dbContext)).HandleAsync(
                new CreateFulfilmentCommand(
                    orderId,
                    paymentId,
                    "SW-00100042",
                    [
                        new CreateFulfilmentLine(bikeLineId, "BIKE-001", "Road Bike", 2),
                        new CreateFulfilmentLine(helmetLineId, "HELMET-001", "Helmet", 1)
                    ],
                    ConfirmedAtUtc),
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // The helmet is cancelled before preparation; the bike cancellation comes too late.
        await using (var dbContext = CreateDbContext(connectionString))
        {
            var accepted = await new CancelFulfilmentHandler(new EfFulfilmentRequestRepository(dbContext))
                .HandleAsync(
                    new CancelFulfilmentCommand(
                        orderId, acceptedCancellationId, [new CancelFulfilmentLine(helmetLineId, 1)],
                        ConfirmedAtUtc.AddMinutes(1)),
                    cancellationToken);

            Assert.Equal(FulfilmentCancellationOutcome.Accepted, accepted.Outcome);
        }

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var units = new CreateFulfilmentUnitHandler(new EfFulfilmentRequestRepository(dbContext));

            await units.HandleAsync(
                new CreateFulfilmentUnitCommand(
                    orderId, firstUnitId, [new CreateFulfilmentUnitLine(bikeLineId, 1)], ConfirmedAtUtc.AddMinutes(2)),
                cancellationToken);

            await units.HandleAsync(
                new CreateFulfilmentUnitCommand(
                    orderId, secondUnitId, [new CreateFulfilmentUnitLine(bikeLineId, 1)], ConfirmedAtUtc.AddMinutes(3)),
                cancellationToken);
        }

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var rejected = await new CancelFulfilmentHandler(new EfFulfilmentRequestRepository(dbContext))
                .HandleAsync(
                    new CancelFulfilmentCommand(
                        orderId, rejectedCancellationId, [new CancelFulfilmentLine(bikeLineId, 1)],
                        ConfirmedAtUtc.AddMinutes(4)),
                    cancellationToken);

            Assert.Equal(FulfilmentCancellationOutcome.Rejected, rejected.Outcome);
        }

        await using (var dbContext = CreateDbContext(connectionString))
        {
            await new CreateShipmentHandler(
                    new EfFulfilmentRequestRepository(dbContext), new EfFulfilmentOutboxStore(dbContext))
                .HandleAsync(
                    new CreateShipmentCommand(
                        orderId, shipmentId, [firstUnitId, secondUnitId], Guid.NewGuid(), ConfirmedAtUtc.AddMinutes(5)),
                    cancellationToken);
        }

        Guid captureRequestId;

        await using (var dbContext = CreateDbContext(connectionString))
        {
            var dispatched = await new DispatchShipmentHandler(
                    new EfFulfilmentRequestRepository(dbContext), new EfFulfilmentOutboxStore(dbContext))
                .HandleAsync(
                    new DispatchShipmentCommand(orderId, shipmentId, Guid.NewGuid(), ConfirmedAtUtc.AddMinutes(6)),
                    cancellationToken);

            captureRequestId = dispatched.PaymentCaptureRequestId
                               ?? throw new InvalidOperationException("Dispatch did not request payment capture.");
        }

        await using var queriesContext = CreateDbContext(connectionString);
        var queries = new EfOperationsFulfilmentQueries(queriesContext);

        var awaitingCapture = await queries.GetByOrderIdAsync(orderId, cancellationToken);

        Assert.NotNull(awaitingCapture);
        Assert.Equal(FulfilmentPaymentCaptureStatus.Pending, awaitingCapture.Capture.Status);
        Assert.Equal(captureRequestId, awaitingCapture.Capture.RequestId);
        Assert.Equal(ConfirmedAtUtc.AddMinutes(6), awaitingCapture.Capture.RequestedAtUtc);
        Assert.Null(awaitingCapture.Capture.ProviderReference);

        await using (var dbContext = CreateDbContext(connectionString))
        {
            await new ApplyPaymentCaptureOutcomeHandler(new EfFulfilmentRequestRepository(dbContext)).HandleAsync(
                new ApplyPaymentCaptureOutcomeCommand(
                    captureRequestId, orderId, paymentId, FulfilmentPaymentCaptureStatus.Captured, "sim-capture-001",
                    ConfirmedAtUtc.AddMinutes(7)),
                cancellationToken);
        }

        await using (var dbContext = CreateDbContext(connectionString))
        {
            await new DeliverShipmentHandler(
                    new EfFulfilmentRequestRepository(dbContext), new EfFulfilmentOutboxStore(dbContext))
                .HandleAsync(
                    new DeliverShipmentCommand(orderId, shipmentId, Guid.NewGuid(), ConfirmedAtUtc.AddMinutes(30)),
                    cancellationToken);
        }

        queriesContext.ChangeTracker.Clear();
        var view = await queries.GetByOrderIdAsync(orderId, cancellationToken);

        Assert.NotNull(view);
        Assert.Equal(orderId, view.OrderId);
        Assert.Equal(paymentId, view.PaymentId);
        // Fulfilment's status compares prepared with ordered quantity and ignores cancellations,
        // so the cancelled helmet keeps the request PartiallyPrepared.
        Assert.Equal(FulfilmentRequestStatus.PartiallyPrepared, view.Status);
        Assert.Equal(ConfirmedAtUtc, view.ConfirmedAtUtc);

        Assert.Equal(FulfilmentPaymentCaptureStatus.Captured, view.Capture.Status);
        Assert.Equal(captureRequestId, view.Capture.RequestId);
        Assert.Equal("sim-capture-001", view.Capture.ProviderReference);
        Assert.Equal(ConfirmedAtUtc.AddMinutes(7), view.Capture.ResolvedAtUtc);

        Assert.Collection(
            view.Lines,
            bike =>
            {
                Assert.Equal(bikeLineId, bike.OrderLineId);
                Assert.Equal(2, bike.Quantity);
                Assert.Equal(2, bike.PreparedQuantity);
                Assert.Equal(0, bike.CancelledQuantity);
            },
            helmet =>
            {
                Assert.Equal(helmetLineId, helmet.OrderLineId);
                Assert.Equal(1, helmet.Quantity);
                Assert.Equal(0, helmet.PreparedQuantity);
                Assert.Equal(1, helmet.CancelledQuantity);
            });

        var shipment = Assert.Single(view.Shipments);
        Assert.Equal(shipmentId, shipment.ShipmentId);
        Assert.Equal(2, shipment.ItemQuantity);
        Assert.Equal(ConfirmedAtUtc.AddMinutes(5), shipment.CreatedAtUtc);
        Assert.Equal(ConfirmedAtUtc.AddMinutes(6), shipment.DispatchedAtUtc);
        Assert.Equal(ConfirmedAtUtc.AddMinutes(30), shipment.DeliveredAtUtc);

        Assert.Collection(
            view.Cancellations,
            accepted =>
            {
                Assert.Equal(acceptedCancellationId, accepted.CancellationId);
                Assert.Equal(FulfilmentCancellationOutcome.Accepted, accepted.Outcome);
                Assert.Null(accepted.RejectionReason);
                Assert.Equal(1, accepted.Quantity);
            },
            rejected =>
            {
                Assert.Equal(rejectedCancellationId, rejected.CancellationId);
                Assert.Equal(FulfilmentCancellationOutcome.Rejected, rejected.Outcome);
                Assert.Equal(
                    FulfilmentCancellationRejectionReason.QuantityNoLongerCancellable, rejected.RejectionReason);
            });

        Assert.Null(await queries.GetByOrderIdAsync(Guid.NewGuid(), cancellationToken));
        Assert.Null(await queries.GetByOrderIdAsync(Guid.Empty, cancellationToken));
    }

    private static FulfilmentDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FulfilmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FulfilmentDbContext(options);
    }
}
