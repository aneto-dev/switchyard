using Switchyard.Ordering.Application.Ports;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderingUnitOfWork : IOrderingUnitOfWork
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderingUnitOfWork(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
