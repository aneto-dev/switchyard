using Microsoft.EntityFrameworkCore;
using Switchyard.Returns.Application.Operations;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfOperationsReturnsQueries : IOperationsReturnsQueries
{
    private readonly ReturnsDbContext _dbContext;

    public EfOperationsReturnsQueries(ReturnsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OperationsReturnsView?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return null;
        }

        var eligibility = await _dbContext.Eligibilities
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.ReturnRequests).ThenInclude(request => request.Lines)
            .Include(candidate => candidate.ReturnReceipts).ThenInclude(receipt => receipt.Lines)
            .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId, cancellationToken);

        if (eligibility is null)
        {
            return null;
        }

        var refunds = await _dbContext.ReturnRefunds
            .AsNoTracking()
            .Where(refund => refund.OrderId == orderId)
            .ToDictionaryAsync(refund => refund.ReturnReceiptId, cancellationToken);

        var lines = eligibility.Lines
            .OrderBy(line => line.OrderLineId)
            .Select(line => new OperationsReturnLine(
                line.OrderLineId,
                line.DeliveredQuantity,
                line.AuthorisedReturnQuantity,
                line.ReturnedQuantity))
            .ToArray();

        var requests = eligibility.ReturnRequests
            .OrderBy(request => request.RequestedAtUtc)
            .Select(request => new OperationsReturnRequest(
                request.ReturnRequestId,
                (ReturnRequestStatus)request.Status,
                request.Lines.Sum(line => line.Quantity),
                request.RequestedAtUtc,
                eligibility.ReturnReceipts
                    .Where(receipt => receipt.ReturnRequestId == request.ReturnRequestId)
                    .OrderBy(receipt => receipt.ReceivedAtUtc)
                    .Select(receipt => new OperationsReturnReceipt(
                        receipt.ReturnReceiptId,
                        receipt.Lines.Sum(line => line.Quantity),
                        receipt.ReceivedAtUtc,
                        refunds.TryGetValue(receipt.ReturnReceiptId, out var refund)
                            ? new OperationsReturnRefund(
                                (ReturnRefundStatus)refund.Status,
                                refund.Amount,
                                refund.Currency,
                                refund.ProviderReference,
                                refund.RequestedAtUtc,
                                refund.UpdatedAtUtc)
                            : null))
                    .ToArray()))
            .ToArray();

        return new OperationsReturnsView(eligibility.OrderId, lines, requests);
    }
}
