using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Reconciliation;
using Switchyard.Payments.Domain.Authorisation;
using Xunit;

namespace Switchyard.Payments.Application.Tests;

public sealed class ReconcilePaymentAuthorisationHandlerTests
{
    [Fact]
    public async Task IndeterminateAuthorisationUsesProviderTruth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 14, 0, 0, TimeSpan.Zero);
        var current = CreateState(PaymentAuthorisationStatus.Indeterminate);
        var applied = current with
        {
            Status = PaymentAuthorisationStatus.Authorised,
            ProviderReference = "provider-auth-1",
            ReconciliationAttemptCount = 1,
            LastReconciledAtUtc = now
        };
        var store = new RecordingStore(
            current,
            new PaymentAuthorisationReconciliationDecision(applied, Applied: true));
        var provider = new RecordingProvider(
            new PaymentProviderReconciliationResult(
                PaymentProviderReconciliationOutcome.Authorised,
                "provider-auth-1"));
        var handler = new ReconcilePaymentAuthorisationHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(current.RequestId),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Authorised, result.Status);
        Assert.Equal("provider-auth-1", result.ProviderReference);
        Assert.Equal(1, result.ReconciliationAttemptCount);
        Assert.False(result.Replayed);
        Assert.Equal(1, provider.InvocationCount);
        Assert.NotNull(store.Applied);
    }

    [Fact]
    public async Task UnknownProviderTruthRemainsIndeterminate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 19, 14, 5, 0, TimeSpan.Zero);
        var current = CreateState(PaymentAuthorisationStatus.Indeterminate);
        var applied = current with
        {
            ReconciliationAttemptCount = 1,
            LastReconciledAtUtc = now
        };
        var store = new RecordingStore(
            current,
            new PaymentAuthorisationReconciliationDecision(applied, Applied: true));
        var provider = new RecordingProvider(
            new PaymentProviderReconciliationResult(
                PaymentProviderReconciliationOutcome.Unknown,
                null));
        var handler = new ReconcilePaymentAuthorisationHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(current.RequestId),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, result.Status);
        Assert.Equal(1, result.ReconciliationAttemptCount);
        Assert.False(result.Replayed);
        Assert.Equal(1, provider.InvocationCount);
    }

    [Fact]
    public async Task DefiniteAuthorisationDoesNotCallProviderAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var current = CreateState(
            PaymentAuthorisationStatus.Authorised,
            providerReference: "provider-auth-2");
        var store = new RecordingStore(
            current,
            new PaymentAuthorisationReconciliationDecision(current, Applied: false));
        var provider = new RecordingProvider(
            new PaymentProviderReconciliationResult(
                PaymentProviderReconciliationOutcome.Authorised,
                "unused"));
        var handler = new ReconcilePaymentAuthorisationHandler(
            store,
            provider,
            TimeProvider.System);

        var result = await handler.HandleAsync(
            new ReconcilePaymentAuthorisationCommand(current.RequestId),
            cancellationToken);

        Assert.True(result.Replayed);
        Assert.Equal(PaymentAuthorisationStatus.Authorised, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(store.Applied);
    }

    [Fact]
    public async Task MissingAuthorisationIsReportedBeforeProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new RecordingStore(null, null);
        var provider = new RecordingProvider(
            new PaymentProviderReconciliationResult(
                PaymentProviderReconciliationOutcome.Unknown,
                null));
        var handler = new ReconcilePaymentAuthorisationHandler(
            store,
            provider,
            TimeProvider.System);
        var requestId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PaymentAuthorisationNotFoundException>(
            () => handler.HandleAsync(
                new ReconcilePaymentAuthorisationCommand(requestId),
                cancellationToken));

        Assert.Equal(requestId, exception.RequestId);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(store.Applied);
    }

    private static PaymentAuthorisationReconciliationState CreateState(
        PaymentAuthorisationStatus status, string? providerReference = null)
    {
        var now = new DateTimeOffset(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);

        return new PaymentAuthorisationReconciliationState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "GBP",
            status,
            "switchyard-auth-test",
            providerReference,
            now,
            now,
            0,
            null);
    }

    private sealed class RecordingStore : IPaymentAuthorisationReconciliationStore
    {
        private readonly PaymentAuthorisationReconciliationState? _current;
        private readonly PaymentAuthorisationReconciliationDecision? _decision;

        public RecordingStore(
            PaymentAuthorisationReconciliationState? current,
            PaymentAuthorisationReconciliationDecision? decision)
        {
            _current = current;
            _decision = decision;
        }

        public PaymentAuthorisationReconciliation? Applied { get; private set; }

        public Task<PaymentAuthorisationReconciliationState?> GetAsync(
            Guid requestId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_current);
        }

        public Task<PaymentAuthorisationReconciliationDecision> ApplyAsync(
            PaymentAuthorisationReconciliation reconciliation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Applied = reconciliation;

            return Task.FromResult(
                _decision ?? throw new InvalidOperationException("No reconciliation decision configured."));
        }
    }

    private sealed class RecordingProvider : IPaymentAuthorisationReconciliationProvider
    {
        private readonly PaymentProviderReconciliationResult _result;

        public RecordingProvider(PaymentProviderReconciliationResult result)
        {
            _result = result;
        }

        public int InvocationCount { get; private set; }

        public Task<PaymentProviderReconciliationResult> ReconcileAsync(
            PaymentProviderReconciliationRequest request,
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
