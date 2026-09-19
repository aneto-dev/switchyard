using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Payments;
using Switchyard.Payments.Domain.Settlement;
using Xunit;

namespace Switchyard.Payments.Application.Tests;

public sealed class ExecutePaymentSettlementHandlerTests
{
    [Fact]
    public async Task CaptureCompletesWithProviderReference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 16, 30, 0, TimeSpan.Zero);
        var pending = CreateDecision(PaymentSettlementStatus.Pending, null, now);
        var completed = CreateDecision(
            PaymentSettlementStatus.Succeeded,
            "provider-capture-1",
            now);
        var store = new RecordingStore(pending, completed);
        var provider = new RecordingProvider(
            new PaymentProviderSettlementResult("provider-capture-1"));
        var handler = new ExecutePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                pending.Attempt.RequestId,
                pending.Attempt.PaymentId.Value,
                PaymentSettlementAction.Capture),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, result.Status);
        Assert.Equal("provider-capture-1", result.ProviderReference);
        Assert.Equal(1, provider.InvocationCount);
        Assert.NotNull(store.Completion);
    }

    [Fact]
    public async Task ProviderTimeoutBecomesIndeterminate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 16, 35, 0, TimeSpan.Zero);
        var pending = CreateDecision(PaymentSettlementStatus.Pending, null, now);
        var indeterminate = CreateDecision(
            PaymentSettlementStatus.Indeterminate,
            null,
            now);
        var store = new RecordingStore(pending, indeterminate);
        var provider = new RecordingProvider(
            new PaymentSettlementProviderIndeterminateException());
        var handler = new ExecutePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                pending.Attempt.RequestId,
                pending.Attempt.PaymentId.Value,
                pending.Attempt.Action),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Indeterminate, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.NotNull(store.Completion);
        Assert.Equal(PaymentSettlementStatus.Indeterminate, store.Completion!.Status);
    }

    [Fact]
    public async Task CompletedReplayDoesNotCallProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 16, 40, 0, TimeSpan.Zero);
        var completed = CreateDecision(
            PaymentSettlementStatus.Succeeded,
            "provider-void-1",
            now,
            replayed: true,
            action: PaymentSettlementAction.Void);
        var store = new RecordingStore(completed, completed);
        var provider = new RecordingProvider(
            new PaymentProviderSettlementResult("unused"));
        var handler = new ExecutePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ExecutePaymentSettlementCommand(
                completed.Attempt.RequestId,
                completed.Attempt.PaymentId.Value,
                completed.Attempt.Action),
            cancellationToken);

        Assert.True(result.Replayed);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(store.Completion);
    }

    [Fact]
    public async Task InvalidActionIsRejectedBeforeDependenciesAreCalled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 16, 45, 0, TimeSpan.Zero);
        var pending = CreateDecision(PaymentSettlementStatus.Pending, null, now);
        var store = new RecordingStore(pending, pending);
        var provider = new RecordingProvider(
            new PaymentProviderSettlementResult("unused"));
        var handler = new ExecutePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(
                new ExecutePaymentSettlementCommand(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    (PaymentSettlementAction)999),
                cancellationToken));

        Assert.Equal(0, store.BeginInvocationCount);
        Assert.Equal(0, provider.InvocationCount);
    }

    private static PaymentSettlementDecision CreateDecision(
        PaymentSettlementStatus status,
        string? providerReference,
        DateTimeOffset now,
        bool replayed = false,
        PaymentSettlementAction action = PaymentSettlementAction.Capture)
    {
        DateTimeOffset? resolvedAt = status == PaymentSettlementStatus.Pending
            ? null
            : now;

        var attempt = new PaymentSettlementAttempt(
            Guid.NewGuid(),
            PaymentId.New(),
            action,
            status,
            "switchyard-settlement-test",
            providerReference,
            now,
            resolvedAt);

        return new PaymentSettlementDecision(
            attempt,
            Guid.NewGuid(),
            100m,
            "GBP",
            "provider-auth-test",
            replayed);
    }

    private sealed class RecordingStore : IPaymentSettlementStore
    {
        private readonly PaymentSettlementDecision _beginDecision;
        private readonly PaymentSettlementDecision _completeDecision;

        public RecordingStore(
            PaymentSettlementDecision beginDecision,
            PaymentSettlementDecision completeDecision)
        {
            _beginDecision = beginDecision;
            _completeDecision = completeDecision;
        }

        public int BeginInvocationCount { get; private set; }

        public PaymentSettlementCompletion? Completion { get; private set; }

        public Task<PaymentSettlementDecision> BeginAsync(
            PaymentSettlementRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginInvocationCount++;

            return Task.FromResult(_beginDecision);
        }

        public Task<PaymentSettlementDecision> CompleteAsync(
            PaymentSettlementCompletion completion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completion = completion;

            return Task.FromResult(_completeDecision);
        }
    }

    private sealed class RecordingProvider : IPaymentSettlementProvider
    {
        private readonly PaymentProviderSettlementResult? _result;
        private readonly PaymentSettlementProviderIndeterminateException? _exception;

        public RecordingProvider(PaymentProviderSettlementResult result)
        {
            _result = result;
        }

        public RecordingProvider(PaymentSettlementProviderIndeterminateException exception)
        {
            _exception = exception;
        }

        public int InvocationCount { get; private set; }

        public Task<PaymentProviderSettlementResult> ExecuteAsync(
            PaymentProviderSettlementRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
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
