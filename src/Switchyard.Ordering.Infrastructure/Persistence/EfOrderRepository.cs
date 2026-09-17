using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderRepository : IOrderRepository
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderRepository(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var record = new OrderRecord
        {
            Id = order.Id.Value,
            OrderNumber = order.OrderNumber.Value,
            CreatedAtUtc = order.CreatedAtUtc,
            Status = order.Status
        };

        for (var position = 0; position < order.Lines.Count; position++)
        {
            var line = order.Lines[position];

            record.Lines.Add(
                new OrderLineRecord
                {
                    Id = line.Id.Value,
                    OrderId = order.Id.Value,
                    Position = position,
                    SkuCode = line.Product.SkuCode.Value,
                    ProductName = line.Product.ProductName,
                    Quantity = line.Quantity,
                    UnitPriceAmount = line.UnitPrice.Amount,
                    Currency = line.UnitPrice.Currency
                });
        }

        await _dbContext.Orders.AddAsync(record, cancellationToken);
    }

    public async Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var record = await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == orderId.Value, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var lines = record.Lines
            .OrderBy(line => line.Position)
            .Select(line => OrderLine.Create(
                new OrderLineId(line.Id),
                new ProductSnapshot(new SkuCode(line.SkuCode), line.ProductName),
                line.Quantity,
                new Money(line.UnitPriceAmount, line.Currency)))
            .ToArray();

        return Order.Rehydrate(
            new OrderId(record.Id),
            new OrderNumber(record.OrderNumber),
            lines,
            record.CreatedAtUtc,
            record.Status);
    }
}
