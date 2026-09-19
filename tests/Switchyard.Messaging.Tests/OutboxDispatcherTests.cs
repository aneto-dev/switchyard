using Switchyard.Messaging;
using Xunit;

namespace Switchyard.Messaging.Tests;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task SuccessfulPublishMarksMessagePublished()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);
        var message = CreateMessage(now);
        var claimed = new ClaimedOutboxMessage(message, Guid.NewGuid(), 1);
        var store = new RecordingStore([claimed]);
        var transport = new RecordingTransport();
        var dispatcher = CreateDispatcher(store, transport, now);

        var result = await dispatcher.DispatchBatchAsync(cancellationToken);

        Assert.Equal(new OutboxDispatchResult(1, 1, 0), result);
        Assert.Equal(message, Assert.Single(transport.Published));
        Assert.Equal(message.MessageId, store.PublishedMessageId);
        Assert.Equal(claimed.LockToken, store.PublishedLockToken);
        Assert.Null(store.FailedMessageId);
    }

    [Fact]
    public async Task FailedPublishSchedulesRetryAndContinuesBatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 20, 5, 0, TimeSpan.Zero);
        var first = new ClaimedOutboxMessage(
            CreateMessage(now, "ordering.order-accepted.v1"),
            Guid.NewGuid(),
            1);
        var second = new ClaimedOutboxMessage(
            CreateMessage(now, "ordering.order-accepted.v1"),
            Guid.NewGuid(),
            1);
        var store = new RecordingStore([first, second]);
        var transport = new RecordingTransport(failMessageId: first.Message.MessageId);
        var dispatcher = CreateDispatcher(store, transport, now);

        var result = await dispatcher.DispatchBatchAsync(cancellationToken);

        Assert.Equal(new OutboxDispatchResult(2, 1, 1), result);
        Assert.Equal(2, transport.Published.Count);
        Assert.Equal(first.Message.MessageId, store.FailedMessageId);
        Assert.Equal("InvalidOperationException", store.FailureKind);
        Assert.Equal(now.AddSeconds(30), store.NextAttemptAtUtc);
        Assert.Equal(second.Message.MessageId, store.PublishedMessageId);
    }

    [Fact]
    public async Task PublishThenMarkFailurePropagatesAndLeavesAtLeastOnceWindowVisible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 20, 10, 0, TimeSpan.Zero);
        var message = CreateMessage(now);
        var claimed = new ClaimedOutboxMessage(message, Guid.NewGuid(), 1);
        var store = new RecordingStore([claimed])
        {
            ThrowWhenMarkingPublished = true
        };
        var transport = new RecordingTransport();
        var dispatcher = CreateDispatcher(store, transport, now);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchBatchAsync(cancellationToken));

        Assert.Equal(message, Assert.Single(transport.Published));
        Assert.Null(store.PublishedMessageId);
        Assert.Null(store.FailedMessageId);
    }

    private static OutboxDispatcher CreateDispatcher(
        RecordingStore store,
        RecordingTransport transport,
        DateTimeOffset now)
    {
        return new OutboxDispatcher(
            store,
            transport,
            new FixedTimeProvider(now),
            new OutboxDispatchOptions(
                batchSize: 10,
                leaseDuration: TimeSpan.FromMinutes(1),
                retryDelay: TimeSpan.FromSeconds(30)));
    }

    private static IntegrationMessageEnvelope CreateMessage(
        DateTimeOffset now,
        string messageType = "test.message.v1")
    {
        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            messageType,
            """{"value":"test"}""",
            now,
            Guid.NewGuid(),
            null);
    }

    private sealed class RecordingStore : IOutboxStore
    {
        private readonly IReadOnlyList<ClaimedOutboxMessage> _claimed;

        public RecordingStore(IReadOnlyList<ClaimedOutboxMessage> claimed)
        {
            _claimed = claimed;
        }

        public bool ThrowWhenMarkingPublished { get; init; }

        public Guid? PublishedMessageId { get; private set; }

        public Guid? PublishedLockToken { get; private set; }

        public Guid? FailedMessageId { get; private set; }

        public DateTimeOffset? NextAttemptAtUtc { get; private set; }

        public string? FailureKind { get; private set; }

        public Task AddAsync(
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimPendingAsync(
            int batchSize,
            DateTimeOffset nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_claimed);
        }

        public Task MarkPublishedAsync(
            Guid messageId,
            Guid lockToken,
            DateTimeOffset publishedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowWhenMarkingPublished)
            {
                throw new InvalidOperationException("Simulated commit failure.");
            }

            PublishedMessageId = messageId;
            PublishedLockToken = lockToken;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid messageId,
            Guid lockToken,
            DateTimeOffset failedAtUtc,
            DateTimeOffset nextAttemptAtUtc,
            string failureKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FailedMessageId = messageId;
            NextAttemptAtUtc = nextAttemptAtUtc;
            FailureKind = failureKind;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransport : IMessageTransport
    {
        private readonly Guid? _failMessageId;

        public RecordingTransport(Guid? failMessageId = null)
        {
            _failMessageId = failMessageId;
        }

        public List<IntegrationMessageEnvelope> Published { get; } = [];

        public Task PublishAsync(
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(message);

            if (message.MessageId == _failMessageId)
            {
                throw new InvalidOperationException("Simulated transport failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
