using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;
using Switchyard.Payments.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class PaymentsOutboxDispatchCycle :
    IPaymentsOutboxDispatchCycle
{
    private readonly IDbContextFactory<PaymentsDbContext> _dbContextFactory;
    private readonly IMessageTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentsOutboxWorkerOptions _options;

    public PaymentsOutboxDispatchCycle(
        IDbContextFactory<PaymentsDbContext> dbContextFactory,
        IMessageTransport transport,
        TimeProvider timeProvider,
        PaymentsOutboxWorkerOptions options)
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
            new EfPaymentsOutboxStore(dbContext);

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
