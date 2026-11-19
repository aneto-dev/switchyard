using Microsoft.EntityFrameworkCore;
using Switchyard.Fulfilment.Application.Operations;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

public sealed class EfOperationsFulfilmentQueries : IOperationsFulfilmentQueries
{
    private readonly FulfilmentDbContext _dbContext;

    public EfOperationsFulfilmentQueries(FulfilmentDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OperationsFulfilmentView?> GetByOrderIdAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return null;
        }

        var request = await _dbContext.Requests
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.Units).ThenInclude(unit => unit.Lines)
            .Include(candidate => candidate.Shipments).ThenInclude(shipment => shipment.Units)
            .Include(candidate => candidate.Cancellations).ThenInclude(cancellation => cancellation.Lines)
            .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId, cancellationToken);

        if (request is null)
        {
            return null;
        }

        var unitQuantities = request.Units.ToDictionary(
            unit => unit.UnitId,
            unit => unit.Lines.Sum(line => line.Quantity));

        var lines = request.Lines
            .OrderBy(line => line.Position)
            .Select(line => new OperationsFulfilmentLine(
                line.OrderLineId,
                line.SkuCode,
                line.ProductName,
                line.Quantity,
                line.PreparedQuantity,
                line.CancelledQuantity))
            .ToArray();

        var shipments = request.Shipments
            .OrderBy(shipment => shipment.CreatedAtUtc)
            .Select(shipment => new OperationsFulfilmentShipment(
                shipment.ShipmentId,
                shipment.Units.Sum(unit => unitQuantities.GetValueOrDefault(unit.UnitId)),
                shipment.CreatedAtUtc,
                shipment.DispatchedAtUtc,
                shipment.DeliveredAtUtc))
            .ToArray();

        var cancellations = request.Cancellations
            .OrderBy(cancellation => cancellation.RequestedAtUtc)
            .Select(cancellation => new OperationsFulfilmentCancellation(
                cancellation.CancellationId,
                cancellation.Outcome,
                cancellation.RejectionReason,
                cancellation.Lines.Sum(line => line.Quantity),
                cancellation.RequestedAtUtc))
            .ToArray();

        return new OperationsFulfilmentView(
            request.OrderId,
            request.Status,
            request.ConfirmedAtUtc,
            request.PaymentId,
            new OperationsFulfilmentCapture(
                request.PaymentCaptureStatus,
                request.PaymentCaptureRequestId,
                request.PaymentCaptureRequestedAtUtc,
                request.PaymentCaptureProviderReference,
                request.PaymentCaptureResolvedAtUtc),
            lines,
            shipments,
            cancellations);
    }
}
