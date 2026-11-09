using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class ReturnsOutboxDispatchCycle :
    IReturnsOutboxDispatchCycle
{
    private readonly IDbContextFactory<ReturnsDbContext> _dbContextFactory;
    private readonly IMessageTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly ReturnsOutboxWorkerOptions _options;

    public ReturnsOutboxDispatchCycle(
        IDbContextFactory<ReturnsDbContext> dbContextFactory,
        IMessageTransport transport,
        TimeProvider timeProvider,
        ReturnsOutboxWorkerOptions options)
    {
        _dbContextFactory =
            dbContextFactory ??
            throw new ArgumentNullException(
                nameof(dbContextFactory));

        _transport =
            transport ??
            throw new ArgumentNullException(
                nameof(transport));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(
                nameof(timeProvider));

        _options =
            options ??
            throw new ArgumentNullException(
                nameof(options));
    }

    public async Task<OutboxDispatchResult> DispatchAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var dispatcher =
            new OutboxDispatcher(
                new EfReturnsOutboxStore(
                    dbContext),
                _transport,
                _timeProvider,
                _options.Dispatch);

        return await dispatcher.DispatchBatchAsync(
            cancellationToken);
    }
}
