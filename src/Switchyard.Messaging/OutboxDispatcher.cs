namespace Switchyard.Messaging;

public sealed class OutboxDispatcher
{
    private readonly IOutboxStore _store;
    private readonly IMessageTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxDispatchOptions _options;

    public OutboxDispatcher(
        IOutboxStore store,
        IMessageTransport transport,
        TimeProvider timeProvider,
        OutboxDispatchOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OutboxDispatchResult> DispatchBatchAsync(
        CancellationToken cancellationToken)
    {
        var claimedAtUtc = _timeProvider.GetUtcNow();
        var claimed = await _store.ClaimPendingAsync(
            _options.BatchSize,
            claimedAtUtc,
            _options.LeaseDuration,
            cancellationToken);

        var published = 0;
        var failed = 0;

        foreach (var item in claimed)
        {
            try
            {
                await _transport.PublishAsync(item.Message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failedAtUtc = _timeProvider.GetUtcNow();

                await _store.MarkFailedAsync(
                    item.Message.MessageId,
                    item.LockToken,
                    failedAtUtc,
                    failedAtUtc.Add(_options.RetryDelay),
                    exception.GetType().Name,
                    cancellationToken);

                failed++;
                continue;
            }

            // A successful provider publish and a failed outbox acknowledgement is the
            // at-least-once crash window. Do not convert it into a transport failure:
            // leave the lease intact so expiry can make the message claimable again.
            await _store.MarkPublishedAsync(
                item.Message.MessageId,
                item.LockToken,
                _timeProvider.GetUtcNow(),
                cancellationToken);

            published++;
        }

        return new OutboxDispatchResult(claimed.Count, published, failed);
    }
}
