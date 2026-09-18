using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderRequestRepository : IOrderRequestRepository
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderRequestRepository(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<AcceptedOrderRequest?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var record = await _dbContext.OrderRequests.AsNoTracking()
                                                  .SingleOrDefaultAsync(
                                                      request => request.IdempotencyKey == idempotencyKey,
                                                      cancellationToken);

        return record is null
            ? null
            : new AcceptedOrderRequest(
                record.IdempotencyKey,
                record.RequestFingerprint,
                new OrderId(record.OrderId),
                record.AcceptedAtUtc);
    }

    public async Task AddAsync(AcceptedOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _dbContext.OrderRequests.AddAsync(
            new OrderRequestRecord
            {
                IdempotencyKey = request.IdempotencyKey,
                RequestFingerprint = request.RequestFingerprint,
                OrderId = request.OrderId.Value,
                AcceptedAtUtc = request.AcceptedAtUtc
            },
            cancellationToken);
    }
}
