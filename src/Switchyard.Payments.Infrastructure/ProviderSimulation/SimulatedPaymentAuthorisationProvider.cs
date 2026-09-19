using System.Collections.Concurrent;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;

namespace Switchyard.Payments.Infrastructure.ProviderSimulation;

public sealed class SimulatedPaymentAuthorisationProvider : IPaymentAuthorisationProvider
{
    private readonly SimulatedPaymentAuthorisationScenario _scenario;
    private readonly ConcurrentDictionary<string, SimulatedAuthorisation> _authorisations =
        new(StringComparer.Ordinal);
    private int _invocationCount;

    public SimulatedPaymentAuthorisationProvider(SimulatedPaymentAuthorisationScenario scenario)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scenario), scenario,
                "Payment provider simulation scenario is not supported.");
        }

        _scenario = scenario;
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public int UniqueAuthorisationCount => _authorisations.Count;

    public Task<PaymentProviderAuthorisationResult> AuthoriseAsync(
        PaymentProviderAuthorisationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Provider idempotency key is required.",
                nameof(request));
        }

        Interlocked.Increment(ref _invocationCount);

        var candidate = new SimulatedAuthorisation(
            request.PaymentId,
            request.OrderId,
            request.Amount,
            request.Currency,
            $"sim-auth-{Guid.NewGuid():N}",
            _scenario);

        var stored = _authorisations.GetOrAdd(request.IdempotencyKey, candidate);

        if (stored.PaymentId != request.PaymentId ||
            stored.OrderId != request.OrderId ||
            stored.Amount != request.Amount ||
            !string.Equals(stored.Currency, request.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider idempotency key was reused with different authorisation data.");
        }

        if (stored.Scenario == SimulatedPaymentAuthorisationScenario.Indeterminate)
        {
            throw new PaymentProviderIndeterminateException();
        }

        var outcome = stored.Scenario switch
        {
            SimulatedPaymentAuthorisationScenario.Authorise =>
                PaymentProviderAuthorisationOutcome.Authorised,
            SimulatedPaymentAuthorisationScenario.Decline =>
                PaymentProviderAuthorisationOutcome.Declined,
            _ => throw new InvalidOperationException(
                "Payment provider simulation reached an unsupported scenario.")
        };

        return Task.FromResult(
            new PaymentProviderAuthorisationResult(
                outcome,
                stored.ProviderReference));
    }

    private sealed record SimulatedAuthorisation(
        Guid PaymentId,
        Guid OrderId,
        decimal Amount,
        string Currency,
        string ProviderReference,
        SimulatedPaymentAuthorisationScenario Scenario);
}
