using Microsoft.EntityFrameworkCore;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed class InventoryOutboxDispatchCycle :
    IInventoryOutboxDispatchCycle
{
    private readonly IDbContextFactory<InventoryDbContext> _dbContextFactory;
    private readonly IMessageTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly InventoryOutboxWorkerOptions _options;

    public InventoryOutboxDispatchCycle(
        IDbContextFactory<InventoryDbContext> dbContextFactory,
        IMessageTransport transport,
        TimeProvider timeProvider,
        InventoryOutboxWorkerOptions options)
    {
        _dbContextFactory =
            dbContextFactory ??
            throw new ArgumentNullException(nameof(dbContextFactory));

        _transport =
            transport ??
            throw new ArgumentNullException(nameof(transport));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));

        _options =
            options ??
            throw new ArgumentNullException(nameof(options));
    }

    public async Task<OutboxDispatchResult> DispatchAsync(
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var store =
            new EfInventoryOutboxStore(dbContext);

        var dispatcher =
            new OutboxDispatcher(
                store,
                _transport,
                _timeProvider,
                _options.Dispatch);

        return await dispatcher.DispatchBatchAsync(
            cancellationToken);
    }
}
