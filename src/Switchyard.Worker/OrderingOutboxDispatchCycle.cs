using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class OrderingOutboxDispatchCycle : IOrderingOutboxDispatchCycle
{
    private readonly IDbContextFactory<OrderingDbContext> _dbContextFactory;
    private readonly IMessageTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly OrderingOutboxWorkerOptions _options;

    public OrderingOutboxDispatchCycle(
        IDbContextFactory<OrderingDbContext> dbContextFactory,
        IMessageTransport transport,
        TimeProvider timeProvider,
        OrderingOutboxWorkerOptions options)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OutboxDispatchResult> DispatchAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var store = new EfOrderingOutboxStore(dbContext);
        var dispatcher = new OutboxDispatcher(store, _transport, _timeProvider, _options.Dispatch);

        return await dispatcher.DispatchBatchAsync(cancellationToken);
    }
}
