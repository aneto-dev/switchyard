using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;

namespace Switchyard.Payments.Application.Authorisation;

public sealed class AuthorisePaymentHandler
{
    private readonly IPaymentAuthorisationStore _store;
    private readonly IPaymentAuthorisationProvider _provider;
    private readonly TimeProvider _timeProvider;

    public AuthorisePaymentHandler(
        IPaymentAuthorisationStore store, IPaymentAuthorisationProvider provider,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AuthorisePaymentResult> HandleAsync(
        AuthorisePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestId == Guid.Empty)
        {
            throw new ArgumentException("Payment authorisation request ID cannot be empty.", nameof(command));
        }

        if (command.OrderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(command));
        }

        var amount = new PaymentAmount(command.Amount, command.Currency);
        var request = new PaymentAuthorisationRequest(
            command.RequestId,
            command.OrderId,
            amount,
            _timeProvider.GetUtcNow());

        var started = await _store.BeginAsync(request, cancellationToken);

        if (started.Attempt.Status != PaymentAuthorisationStatus.AuthorisationPending)
        {
            return ToResult(started, started.Replayed);
        }

        // Persist the external side-effect boundary before provider I/O. Once this
        // claim exists, another execution must never blindly invoke Authorise again.
        var invocationClaim = await _store.ClaimProviderInvocationAsync(
            command.RequestId,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        if (invocationClaim.Decision.Attempt.Status !=
            PaymentAuthorisationStatus.AuthorisationPending)
        {
            return ToResult(
                invocationClaim.Decision,
                replayed: true);
        }

        if (!invocationClaim.Acquired)
        {
            // A prior execution crossed (or may have crossed) the provider boundary.
            // Its outcome is therefore unknown until provider truth is reconciled.
            var recovered = await _store.CompleteAsync(
                new PaymentAuthorisationCompletion(
                    command.RequestId,
                    PaymentAuthorisationStatus.Indeterminate,
                    ProviderReference: null,
                    _timeProvider.GetUtcNow()),
                cancellationToken);

            return ToResult(
                recovered,
                replayed: true);
        }

        PaymentAuthorisationCompletion completion;

        try
        {
            var providerResult = await _provider.AuthoriseAsync(
                new PaymentProviderAuthorisationRequest(
                    invocationClaim.Decision.Attempt.ProviderIdempotencyKey,
                    invocationClaim.Decision.Intent.Id.Value,
                    invocationClaim.Decision.Intent.OrderId,
                    invocationClaim.Decision.Intent.Amount.Amount,
                    invocationClaim.Decision.Intent.Amount.Currency),
                cancellationToken);

            var status = providerResult.Outcome switch
            {
                PaymentProviderAuthorisationOutcome.Authorised => PaymentAuthorisationStatus.Authorised,
                PaymentProviderAuthorisationOutcome.Declined => PaymentAuthorisationStatus.Declined,
                _ => throw new InvalidOperationException("Payment provider returned an unsupported outcome.")
            };

            completion = new PaymentAuthorisationCompletion(
                command.RequestId,
                status,
                providerResult.ProviderReference,
                _timeProvider.GetUtcNow());
        }
        catch (PaymentProviderIndeterminateException exception)
        {
            completion = new PaymentAuthorisationCompletion(
                command.RequestId,
                PaymentAuthorisationStatus.Indeterminate,
                exception.ProviderReference,
                _timeProvider.GetUtcNow());
        }

        var completed = await _store.CompleteAsync(completion, cancellationToken);

        return ToResult(
            completed,
            started.Replayed || completed.Replayed);
    }

    private static AuthorisePaymentResult ToResult(
        PaymentAuthorisationDecision decision, bool replayed)
    {
        return new AuthorisePaymentResult(
            decision.Attempt.RequestId,
            decision.Intent.Id.Value,
            decision.Intent.OrderId,
            decision.Intent.Amount.Amount,
            decision.Intent.Amount.Currency,
            decision.Attempt.Status,
            decision.Attempt.ProviderIdempotencyKey,
            decision.Attempt.ProviderReference,
            decision.Attempt.RequestedAtUtc,
            decision.Attempt.ResolvedAtUtc,
            replayed);
    }
}
