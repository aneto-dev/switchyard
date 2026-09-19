using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.SettlementReconciliation;
using Switchyard.Payments.Domain.Settlement;
using Xunit;

namespace Switchyard.Payments.Application.Tests;

public sealed class ReconcilePaymentSettlementHandlerTests
{
    [Fact]
    public async Task IndeterminateSettlementUsesSuccessfulProviderTruth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 18, 30, 0, TimeSpan.Zero);
        var current = CreateState(PaymentSettlementStatus.Indeterminate);
        var applied = current with
        {
            Status = PaymentSettlementStatus.Succeeded,
            ProviderReference = "provider-capture-1",
            ReconciliationAttemptCount = 1,
            LastReconciledAtUtc = now
        };
        var store = new RecordingStore(
            current,
            new PaymentSettlementReconciliationDecision(applied, Applied: true));
        var provider = new RecordingProvider(
            new PaymentProviderSettlementReconciliationResult(
                PaymentProviderSettlementReconciliationOutcome.Succeeded,
                "provider-capture-1"));
        var handler = new ReconcilePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(current.RequestId),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Succeeded, result.Status);
        Assert.Equal("provider-capture-1", result.ProviderReference);
        Assert.Equal(1, result.ReconciliationAttemptCount);
        Assert.False(result.Replayed);
        Assert.Equal(1, provider.InvocationCount);
        Assert.NotNull(store.Applied);
    }

    [Fact]
    public async Task ProviderCanConfirmSettlementWasNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 18, 35, 0, TimeSpan.Zero);
        var current = CreateState(PaymentSettlementStatus.Indeterminate);
        var applied = current with
        {
            Status = PaymentSettlementStatus.NotApplied,
            ReconciliationAttemptCount = 1,
            LastReconciledAtUtc = now
        };
        var store = new RecordingStore(
            current,
            new PaymentSettlementReconciliationDecision(applied, Applied: true));
        var provider = new RecordingProvider(
            new PaymentProviderSettlementReconciliationResult(
                PaymentProviderSettlementReconciliationOutcome.NotApplied,
                null));
        var handler = new ReconcilePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(current.RequestId),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.NotApplied, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task UnknownProviderTruthRemainsIndeterminate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 18, 40, 0, TimeSpan.Zero);
        var current = CreateState(PaymentSettlementStatus.Indeterminate);
        var applied = current with
        {
            ReconciliationAttemptCount = 1,
            LastReconciledAtUtc = now
        };
        var store = new RecordingStore(
            current,
            new PaymentSettlementReconciliationDecision(applied, Applied: true));
        var provider = new RecordingProvider(
            new PaymentProviderSettlementReconciliationResult(
                PaymentProviderSettlementReconciliationOutcome.Unknown,
                null));
        var handler = new ReconcilePaymentSettlementHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(current.RequestId),
            cancellationToken);

        Assert.Equal(PaymentSettlementStatus.Indeterminate, result.Status);
        Assert.Equal(1, result.ReconciliationAttemptCount);
        Assert.Equal(1, provider.InvocationCount);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task DefiniteSettlementDoesNotCallProviderAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var current = CreateState(
            PaymentSettlementStatus.Succeeded,
            providerReference: "provider-void-1");
        var store = new RecordingStore(
            current,
            new PaymentSettlementReconciliationDecision(current, Applied: false));
        var provider = new RecordingProvider(
            new PaymentProviderSettlementReconciliationResult(
                PaymentProviderSettlementReconciliationOutcome.Succeeded,
                "unused"));
        var handler = new ReconcilePaymentSettlementHandler(
            store,
            provider,
            TimeProvider.System);

        var result = await handler.HandleAsync(
            new ReconcilePaymentSettlementCommand(current.RequestId),
            cancellationToken);

        Assert.True(result.Replayed);
        Assert.Equal(PaymentSettlementStatus.Succeeded, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(store.Applied);
    }

    [Fact]
    public async Task MissingSettlementIsReportedBeforeProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new RecordingStore(null, null);
        var provider = new RecordingProvider(
            new PaymentProviderSettlementReconciliationResult(
                PaymentProviderSettlementReconciliationOutcome.Unknown,
                null));
        var handler = new ReconcilePaymentSettlementHandler(
            store,
            provider,
            TimeProvider.System);
        var requestId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PaymentSettlementReconciliationNotFoundException>(
            () => handler.HandleAsync(
                new ReconcilePaymentSettlementCommand(requestId),
                cancellationToken));

        Assert.Equal(requestId, exception.RequestId);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(store.Applied);
    }

    private static PaymentSettlementReconciliationState CreateState(
        PaymentSettlementStatus status,
        string? providerReference = null)
    {
        var now = new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

        return new PaymentSettlementReconciliationState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentSettlementAction.Capture,
            100m,
            "GBP",
            status,
            "switchyard-capture-test",
            providerReference,
            "provider-auth-test",
            now,
            now,
            0,
            null);
    }

    private sealed class RecordingStore : IPaymentSettlementReconciliationStore
    {
        private readonly PaymentSettlementReconciliationState? _current;
        private readonly PaymentSettlementReconciliationDecision? _decision;

        public RecordingStore(
            PaymentSettlementReconciliationState? current,
            PaymentSettlementReconciliationDecision? decision)
        {
            _current = current;
            _decision = decision;
        }

        public PaymentSettlementReconciliation? Applied { get; private set; }

        public Task<PaymentSettlementReconciliationState?> GetAsync(
            Guid requestId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_current);
        }

        public Task<PaymentSettlementReconciliationDecision> ApplyAsync(
            PaymentSettlementReconciliation reconciliation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Applied = reconciliation;

            return Task.FromResult(
                _decision ?? throw new InvalidOperationException(
                    "No settlement reconciliation decision configured."));
        }
    }

    private sealed class RecordingProvider : IPaymentSettlementReconciliationProvider
    {
        private readonly PaymentProviderSettlementReconciliationResult _result;

        public RecordingProvider(PaymentProviderSettlementReconciliationResult result)
        {
            _result = result;
        }

        public int InvocationCount { get; private set; }

        public Task<PaymentProviderSettlementReconciliationResult> ReconcileAsync(
            PaymentProviderSettlementReconciliationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;

            return Task.FromResult(_result);
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
