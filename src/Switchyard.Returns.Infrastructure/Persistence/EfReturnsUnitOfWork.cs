using Microsoft.EntityFrameworkCore.Storage;
using Switchyard.Returns.Application.Requests;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfReturnsUnitOfWork :
    IReturnsUnitOfWork
{
    private readonly ReturnsDbContext _dbContext;

    public EfReturnsUnitOfWork(
        ReturnsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task<IReturnsTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        return new EfReturnsTransaction(
            transaction);
    }

    private sealed class EfReturnsTransaction :
        IReturnsTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfReturnsTransaction(
            IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public ValueTask DisposeAsync() =>
            _transaction.DisposeAsync();

        public Task CommitAsync(
            CancellationToken cancellationToken) =>
            _transaction.CommitAsync(
                cancellationToken);
    }
}
