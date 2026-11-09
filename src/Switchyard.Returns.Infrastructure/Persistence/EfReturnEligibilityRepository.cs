using Microsoft.EntityFrameworkCore;
using Switchyard.Returns.Application.Eligibility;
using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfReturnEligibilityRepository :
    IReturnEligibilityRepository
{
    private readonly ReturnsDbContext _dbContext;

    public EfReturnEligibilityRepository(
        ReturnsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task<ReturnEligibility?> GetByOrderIdForUpdateAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({orderId.ToString()}, 0));",
            cancellationToken);

        var record =
            await _dbContext.Eligibilities
                            .Include(candidate => candidate.Lines)
                            .Include(candidate => candidate.Deliveries)
                                .ThenInclude(delivery => delivery.Lines)
                            .Include(candidate => candidate.ReturnRequests)
                                .ThenInclude(request => request.Lines)
                            .Include(candidate => candidate.ReturnReceipts)
                                .ThenInclude(receipt => receipt.Lines)
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.OrderId ==
                                    orderId,
                                cancellationToken);

        return record is null
            ? null
            : Map(record);
    }

    public async Task SaveAsync(
        ReturnEligibility eligibility,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            eligibility);

        var record =
            _dbContext.Eligibilities.Local.SingleOrDefault(
                candidate =>
                    candidate.OrderId ==
                    eligibility.OrderId);

        if (record is null)
        {
            record =
                new ReturnEligibilityRecord
                {
                    OrderId = eligibility.OrderId,
                    OrderNumber = eligibility.OrderNumber
                };

            await _dbContext.Eligibilities.AddAsync(
                record,
                cancellationToken);
        }

        foreach (var line in eligibility.Lines)
        {
            var persisted =
                record.Lines.SingleOrDefault(
                    candidate =>
                        candidate.OrderLineId ==
                        line.OrderLineId);

            if (persisted is null)
            {
                record.Lines.Add(
                    new ReturnEligibilityLineRecord
                    {
                        OrderId = eligibility.OrderId,
                        OrderLineId = line.OrderLineId,
                        DeliveredQuantity = line.DeliveredQuantity,
                        AuthorisedReturnQuantity =
                            line.AuthorisedReturnQuantity,
                        ReturnedQuantity = line.ReturnedQuantity
                    });
            }
            else
            {
                persisted.DeliveredQuantity =
                    line.DeliveredQuantity;

                persisted.AuthorisedReturnQuantity =
                    line.AuthorisedReturnQuantity;

                persisted.ReturnedQuantity =
                    line.ReturnedQuantity;
            }
        }

        foreach (var delivery in eligibility.Deliveries)
        {
            if (record.Deliveries.Any(
                    candidate =>
                        candidate.ShipmentId ==
                        delivery.ShipmentId))
            {
                continue;
            }

            var persistedDelivery =
                new ShipmentDeliveryRecord
                {
                    OrderId = eligibility.OrderId,
                    ShipmentId = delivery.ShipmentId,
                    DeliveredAtUtc = delivery.DeliveredAtUtc
                };

            foreach (var line in delivery.Lines)
            {
                persistedDelivery.Lines.Add(
                    new ShipmentDeliveryLineRecord
                    {
                        OrderId = eligibility.OrderId,
                        ShipmentId = delivery.ShipmentId,
                        OrderLineId = line.OrderLineId,
                        Quantity = line.Quantity
                    });
            }

            record.Deliveries.Add(
                persistedDelivery);
        }

        foreach (var request in eligibility.ReturnRequests)
        {
            var persistedRequest =
                record.ReturnRequests.SingleOrDefault(
                    candidate =>
                        candidate.ReturnRequestId ==
                        request.ReturnRequestId);

            if (persistedRequest is null)
            {
                persistedRequest =
                    new ReturnRequestRecord
                    {
                        OrderId = eligibility.OrderId,
                        ReturnRequestId = request.ReturnRequestId,
                        RequestedAtUtc = request.RequestedAtUtc,
                        Status = (int)request.Status
                    };

                foreach (var line in request.Lines)
                {
                    persistedRequest.Lines.Add(
                        new ReturnRequestLineRecord
                        {
                            OrderId = eligibility.OrderId,
                            ReturnRequestId = request.ReturnRequestId,
                            OrderLineId = line.OrderLineId,
                            Quantity = line.Quantity
                        });
                }

                record.ReturnRequests.Add(
                    persistedRequest);
            }
            else
            {
                persistedRequest.Status =
                    (int)request.Status;
            }
        }

        foreach (var receipt in eligibility.ReturnReceipts)
        {
            if (record.ReturnReceipts.Any(
                    candidate =>
                        candidate.ReturnReceiptId ==
                        receipt.ReturnReceiptId))
            {
                continue;
            }

            var persistedReceipt =
                new ReturnReceiptRecord
                {
                    OrderId = eligibility.OrderId,
                    ReturnReceiptId = receipt.ReturnReceiptId,
                    ReturnRequestId = receipt.ReturnRequestId,
                    ReceivedAtUtc = receipt.ReceivedAtUtc
                };

            foreach (var line in receipt.Lines)
            {
                persistedReceipt.Lines.Add(
                    new ReturnReceiptLineRecord
                    {
                        OrderId = eligibility.OrderId,
                        ReturnReceiptId = receipt.ReturnReceiptId,
                        OrderLineId = line.OrderLineId,
                        Quantity = line.Quantity
                    });
            }

            record.ReturnReceipts.Add(
                persistedReceipt);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static ReturnEligibility Map(
        ReturnEligibilityRecord record) =>
        ReturnEligibility.Rehydrate(
            record.OrderId,
            record.OrderNumber,
            record.Lines
                  .OrderBy(line => line.OrderLineId)
                  .Select(
                      line =>
                          new ReturnEligibilityLine(
                              line.OrderLineId,
                              line.DeliveredQuantity,
                              line.AuthorisedReturnQuantity,
                              line.ReturnedQuantity)),
            record.Deliveries
                  .OrderBy(delivery => delivery.DeliveredAtUtc)
                  .ThenBy(delivery => delivery.ShipmentId)
                  .Select(
                      delivery =>
                          new ShipmentDeliveryFact(
                              delivery.ShipmentId,
                              delivery.DeliveredAtUtc,
                              delivery.Lines
                                      .OrderBy(line => line.OrderLineId)
                                      .Select(
                                          line =>
                                              new DeliveredLine(
                                                  line.OrderLineId,
                                                  line.Quantity))
                                      .ToArray())),
            record.ReturnRequests
                  .OrderBy(request => request.RequestedAtUtc)
                  .ThenBy(request => request.ReturnRequestId)
                  .Select(
                      request =>
                          new ReturnRequest(
                              request.ReturnRequestId,
                              request.RequestedAtUtc,
                              (ReturnRequestStatus)request.Status,
                              request.Lines
                                     .OrderBy(line => line.OrderLineId)
                                     .Select(
                                         line =>
                                             new ReturnRequestLine(
                                                 line.OrderLineId,
                                                 line.Quantity))
                                     .ToArray())),
            record.ReturnReceipts
                  .OrderBy(receipt => receipt.ReceivedAtUtc)
                  .ThenBy(receipt => receipt.ReturnReceiptId)
                  .Select(
                      receipt =>
                          new ReturnReceipt(
                              receipt.ReturnReceiptId,
                              receipt.ReturnRequestId,
                              receipt.ReceivedAtUtc,
                              receipt.Lines
                                     .OrderBy(line => line.OrderLineId)
                                     .Select(
                                         line =>
                                             new ReturnReceiptLine(
                                                 line.OrderLineId,
                                                 line.Quantity))
                                     .ToArray())));
}
