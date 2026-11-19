using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Operations;
using Switchyard.Ordering.Application.Placement;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOperationsOrderQueries : IOperationsOrderQueries
{
    private readonly OrderingDbContext _dbContext;

    public EfOperationsOrderQueries(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<OperationsOrderSummary>> ListRecentAsync(
        OperationsOrderListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Limit is <= 0 or > OperationsOrderListFilter.MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), filter.Limit, "Limit is out of range.");
        }

        var orders = _dbContext.Orders.AsNoTracking();

        if (filter.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.OrderNumber))
        {
            var orderNumber = filter.OrderNumber.Trim().ToUpperInvariant();
            orders = orders.Where(order => order.OrderNumber == orderNumber);
        }

        var rows = await orders
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Take(filter.Limit)
            .Select(order => new
            {
                order.Id,
                order.OrderNumber,
                order.Status,
                order.CreatedAtUtc,
                TotalAmount = order.Lines.Sum(line => line.Quantity * line.UnitPriceAmount),
                Currency = order.Lines.OrderBy(line => line.Position).Select(line => line.Currency).First(),
                LineCount = order.Lines.Count,
                PlacementState = _dbContext.OrderPlacementProcesses
                    .Where(process => process.OrderId == order.Id)
                    .Select(process => (OrderPlacementState?)process.State)
                    .FirstOrDefault(),
                PlacementUpdatedAtUtc = _dbContext.OrderPlacementProcesses
                    .Where(process => process.OrderId == order.Id)
                    .Select(process => (DateTimeOffset?)process.UpdatedAtUtc)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new OperationsOrderSummary(
                       row.Id,
                       row.OrderNumber,
                       row.Status,
                       row.PlacementState,
                       row.TotalAmount,
                       row.Currency,
                       row.LineCount,
                       row.CreatedAtUtc,
                       row.PlacementUpdatedAtUtc))
                   .ToArray();
    }

    public async Task<OperationsOrderDetail?> GetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
        {
            return null;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var process = await _dbContext.OrderPlacementProcesses
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId, cancellationToken);

        var placementLines = process?.Lines.ToDictionary(line => line.OrderLineId)
                             ?? new Dictionary<Guid, OrderPlacementLineRecord>();

        var lines = order.Lines
            .OrderBy(line => line.Position)
            .Select(line =>
            {
                placementLines.TryGetValue(line.Id, out var placementLine);

                return new OperationsOrderLine(
                    line.Id,
                    line.SkuCode,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPriceAmount,
                    line.Quantity * line.UnitPriceAmount,
                    line.Currency,
                    placementLine?.State,
                    placementLine?.ReservationRequestId,
                    placementLine?.ReservationId,
                    placementLine?.ReservationExpiresAtUtc);
            })
            .ToArray();

        var placement = process is null
            ? null
            : new OperationsOrderPlacement(
                process.State,
                process.StartedAtUtc,
                process.UpdatedAtUtc,
                process.PaymentAuthorisationRequestId);

        return new OperationsOrderDetail(
            order.Id,
            order.OrderNumber,
            order.Status,
            lines.Sum(line => line.LineTotalAmount),
            lines[0].Currency,
            order.CreatedAtUtc,
            placement,
            lines);
    }
}
