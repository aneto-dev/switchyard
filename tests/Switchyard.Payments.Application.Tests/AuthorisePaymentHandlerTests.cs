using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;
using Xunit;

namespace Switchyard.Payments.Application.Tests;

public sealed class AuthorisePaymentHandlerTests
{
    [Fact]
    public async Task AuthorisedProviderResultCompletesRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 18, 17, 0, 0, TimeSpan.Zero);
        var pending = CreateDecision(
            PaymentAuthorisationStatus.AuthorisationPending,
            replayed: false,
            providerReference: null,
            now);
        var authorised = CreateDecision(
            PaymentAuthorisationStatus.Authorised,
            replayed: false,
            providerReference: "provider-auth-1",
            now);
        var store = new RecordingStore(pending, authorised);
        var provider = new RecordingProvider(
            new PaymentProviderAuthorisationResult(
                PaymentProviderAuthorisationOutcome.Authorised,
                "provider-auth-1"));
        var handler = new AuthorisePaymentHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new AuthorisePaymentCommand(
                pending.Attempt.RequestId,
                pending.Intent.OrderId,
                pending.Intent.Amount.Amount,
                pending.Intent.Amount.Currency),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Authorised, result.Status);
        Assert.Equal("provider-auth-1", result.ProviderReference);
        Assert.Equal(1, provider.InvocationCount);
        Assert.NotNull(store.Completion);
        Assert.Equal(PaymentAuthorisationStatus.Authorised, store.Completion!.Status);
    }

    [Fact]
    public async Task UnknownProviderOutcomeBecomesIndeterminate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 18, 17, 5, 0, TimeSpan.Zero);
        var pending = CreateDecision(
            PaymentAuthorisationStatus.AuthorisationPending,
            replayed: false,
            providerReference: null,
            now);
        var indeterminate = CreateDecision(
            PaymentAuthorisationStatus.Indeterminate,
            replayed: false,
            providerReference: null,
            now);
        var store = new RecordingStore(pending, indeterminate);
        var provider = new RecordingProvider(
            new PaymentProviderIndeterminateException());
        var handler = new AuthorisePaymentHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new AuthorisePaymentCommand(
                pending.Attempt.RequestId,
                pending.Intent.OrderId,
                pending.Intent.Amount.Amount,
                pending.Intent.Amount.Currency),
            cancellationToken);

        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, result.Status);
        Assert.Null(result.ProviderReference);
        Assert.NotNull(store.Completion);
        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, store.Completion!.Status);
    }

    [Fact]
    public async Task CompletedReplayDoesNotCallProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 18, 17, 10, 0, TimeSpan.Zero);
        var replay = CreateDecision(
            PaymentAuthorisationStatus.Authorised,
            replayed: true,
            providerReference: "provider-auth-2",
            now);
        var store = new RecordingStore(replay, replay);
        var provider = new RecordingProvider(
            new PaymentProviderAuthorisationResult(
                PaymentProviderAuthorisationOutcome.Authorised,
                "unused"));
        var handler = new AuthorisePaymentHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new AuthorisePaymentCommand(
                replay.Attempt.RequestId,
                replay.Intent.OrderId,
                replay.Intent.Amount.Amount,
                replay.Intent.Amount.Currency),
            cancellationToken);

        Assert.True(result.Replayed);
        Assert.Equal(PaymentAuthorisationStatus.Authorised, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(store.Completion);
    }

    [Fact]
    public async Task InvalidAmountIsRejectedBeforeDependenciesAreCalled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 18, 17, 15, 0, TimeSpan.Zero);
        var pending = CreateDecision(
            PaymentAuthorisationStatus.AuthorisationPending,
            replayed: false,
            providerReference: null,
            now);
        var store = new RecordingStore(pending, pending);
        var provider = new RecordingProvider(
            new PaymentProviderAuthorisationResult(
                PaymentProviderAuthorisationOutcome.Authorised,
                "unused"));
        var handler = new AuthorisePaymentHandler(
            store,
            provider,
            new FixedTimeProvider(now));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(
                new AuthorisePaymentCommand(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    0m,
                    "GBP"),
                cancellationToken));

        Assert.Equal(0, store.BeginInvocationCount);
        Assert.Equal(0, provider.InvocationCount);
    }

    private static PaymentAuthorisationDecision CreateDecision(
        PaymentAuthorisationStatus status, bool replayed,
        string? providerReference, DateTimeOffset now)
    {
        var paymentId = PaymentId.New();
        DateTimeOffset? resolvedAt = status == PaymentAuthorisationStatus.AuthorisationPending
            ? null
            : now;

        var intent = new PaymentIntent(
            paymentId,
            Guid.NewGuid(),
            new PaymentAmount(100m, "GBP"),
            status,
            now,
            resolvedAt);

        var attempt = new PaymentAuthorisationAttempt(
            Guid.NewGuid(),
            paymentId,
            "switchyard-auth-test",
            providerReference,
            status,
            now,
            resolvedAt);

        return new PaymentAuthorisationDecision(intent, attempt, replayed);
    }

    private sealed class RecordingStore : IPaymentAuthorisationStore
    {
        private readonly PaymentAuthorisationDecision _beginDecision;
        private readonly PaymentAuthorisationDecision _completeDecision;

        public RecordingStore(
            PaymentAuthorisationDecision beginDecision,
            PaymentAuthorisationDecision completeDecision)
        {
            _beginDecision = beginDecision;
            _completeDecision = completeDecision;
        }

        public int BeginInvocationCount { get; private set; }

        public PaymentAuthorisationCompletion? Completion { get; private set; }

        public Task<PaymentAuthorisationDecision> BeginAsync(
            PaymentAuthorisationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginInvocationCount++;
            return Task.FromResult(_beginDecision);
        }

        public Task<PaymentAuthorisationDecision> CompleteAsync(
            PaymentAuthorisationCompletion completion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Completion = completion;
            return Task.FromResult(_completeDecision);
        }
    }

    private sealed class RecordingProvider : IPaymentAuthorisationProvider
    {
        private readonly PaymentProviderAuthorisationResult? _result;
        private readonly PaymentProviderIndeterminateException? _exception;

        public RecordingProvider(PaymentProviderAuthorisationResult result)
        {
            _result = result;
        }

        public RecordingProvider(PaymentProviderIndeterminateException exception)
        {
            _exception = exception;
        }

        public int InvocationCount { get; private set; }

        public Task<PaymentProviderAuthorisationResult> AuthoriseAsync(
            PaymentProviderAuthorisationRequest request, CancellationToken cancellationToken)
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
