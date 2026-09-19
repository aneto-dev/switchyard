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

        PaymentAuthorisationCompletion completion;

        try
        {
            var providerResult = await _provider.AuthoriseAsync(
                new PaymentProviderAuthorisationRequest(
                    started.Attempt.ProviderIdempotencyKey,
                    started.Intent.Id.Value,
                    started.Intent.OrderId,
                    started.Intent.Amount.Amount,
                    started.Intent.Amount.Currency),
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

        return ToResult(completed, started.Replayed || completed.Replayed);
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
