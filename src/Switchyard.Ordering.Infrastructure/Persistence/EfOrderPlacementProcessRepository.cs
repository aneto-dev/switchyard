using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderPlacementProcessRepository :
    IOrderPlacementProcessRepository
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderPlacementProcessRepository(
        OrderingDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(
        OrderPlacementProcess process,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        var record = new OrderPlacementProcessRecord
        {
            OrderId = process.OrderId.Value,
            State = process.State,
            StartedAtUtc = process.StartedAtUtc,
            UpdatedAtUtc = process.UpdatedAtUtc
        };

        foreach (var line in process.Lines)
        {
            record.Lines.Add(
                new OrderPlacementLineRecord
                {
                    OrderId = process.OrderId.Value,
                    OrderLineId = line.OrderLineId.Value,
                    ReservationRequestId = line.ReservationRequestId,
                    SkuCode = line.SkuCode,
                    Quantity = line.Quantity,
                    State = line.State,
                    ReservationId = line.ReservationId
                });
        }

        await _dbContext.OrderPlacementProcesses.AddAsync(
            record,
            cancellationToken);
    }

    public async Task<OrderPlacementProcess?> GetByOrderIdAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var record = await _dbContext.OrderPlacementProcesses
            .AsNoTracking()
            .Include(process => process.Lines)
            .SingleOrDefaultAsync(
                process => process.OrderId == orderId.Value,
                cancellationToken);

        if (record is null)
        {
            return null;
        }

        var lines = record.Lines
            .OrderBy(line => line.OrderLineId)
            .Select(line =>
                OrderPlacementLine.Rehydrate(
                    new OrderLineId(line.OrderLineId),
                    line.ReservationRequestId,
                    line.SkuCode,
                    line.Quantity,
                    line.State,
                    line.ReservationId))
            .ToArray();

        return OrderPlacementProcess.Rehydrate(
            new OrderId(record.OrderId),
            record.State,
            record.StartedAtUtc,
            record.UpdatedAtUtc,
            lines);
    }
}
