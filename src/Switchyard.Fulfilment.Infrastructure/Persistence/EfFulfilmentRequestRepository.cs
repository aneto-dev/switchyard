using Microsoft.EntityFrameworkCore;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

public sealed class EfFulfilmentRequestRepository :
    IFulfilmentRequestRepository
{
    private readonly FulfilmentDbContext _dbContext;

    public EfFulfilmentRequestRepository(
        FulfilmentDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<FulfilmentRequest?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var record =
            await _dbContext.Requests
                            .AsNoTracking()
                            .Include(request => request.Lines)
                            .SingleOrDefaultAsync(
                                request =>
                                    request.OrderId ==
                                    orderId,
                                cancellationToken);

        if (record is null)
        {
            return null;
        }

        return FulfilmentRequest.Rehydrate(
            record.OrderId,
            record.OrderNumber,
            record.Lines
                  .OrderBy(line => line.Position)
                  .Select(
                      line =>
                          new FulfilmentRequestLine(
                              line.OrderLineId,
                              line.SkuCode,
                              line.ProductName,
                              line.Quantity)),
            record.ConfirmedAtUtc,
            record.Status);
    }

    public async Task AddAsync(
        FulfilmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record =
            new FulfilmentRequestRecord
            {
                OrderId = request.OrderId,
                OrderNumber = request.OrderNumber,
                Status = request.Status,
                ConfirmedAtUtc = request.ConfirmedAtUtc
            };

        for (var position = 0;
             position < request.Lines.Count;
             position++)
        {
            var line =
                request.Lines[position];

            record.Lines.Add(
                new FulfilmentRequestLineRecord
                {
                    OrderId = request.OrderId,
                    OrderLineId = line.OrderLineId,
                    Position = position,
                    SkuCode = line.SkuCode,
                    ProductName = line.ProductName,
                    Quantity = line.Quantity
                });
        }

        await _dbContext.Requests.AddAsync(
            record,
            cancellationToken);
    }
}
