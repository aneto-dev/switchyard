using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Ports;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderingUnitOfWork : IOrderingUnitOfWork
{
    private const string OrderRequestPrimaryKey = "pk_order_requests";

    private readonly OrderingDbContext _dbContext;

    public EfOrderingUnitOfWork(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateOrderRequest(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateOrderRequestException(exception);
        }
    }

    private static bool IsDuplicateOrderRequest(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OrderRequestPrimaryKey
        };
    }
}
