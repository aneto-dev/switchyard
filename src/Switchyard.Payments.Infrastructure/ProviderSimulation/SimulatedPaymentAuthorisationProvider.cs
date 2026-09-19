using System.Collections.Concurrent;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Reconciliation;

namespace Switchyard.Payments.Infrastructure.ProviderSimulation;

public sealed class SimulatedPaymentAuthorisationProvider :
    IPaymentAuthorisationProvider,
    IPaymentAuthorisationReconciliationProvider
{
    private readonly SimulatedPaymentAuthorisationScenario _scenario;
    private readonly SimulatedPaymentReconciliationScenario _reconciliationScenario;
    private readonly ConcurrentDictionary<string, SimulatedAuthorisation> _authorisations =
        new(StringComparer.Ordinal);
    private int _invocationCount;
    private int _reconciliationInvocationCount;

    public SimulatedPaymentAuthorisationProvider(SimulatedPaymentAuthorisationScenario scenario)
        : this(scenario, DefaultReconciliationScenario(scenario))
    {
    }

    public SimulatedPaymentAuthorisationProvider(
        SimulatedPaymentAuthorisationScenario scenario,
        SimulatedPaymentReconciliationScenario reconciliationScenario)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scenario), scenario,
                "Payment provider simulation scenario is not supported.");
        }

        if (!Enum.IsDefined(reconciliationScenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliationScenario), reconciliationScenario,
                "Payment reconciliation simulation scenario is not supported.");
        }

        _scenario = scenario;
        _reconciliationScenario = reconciliationScenario;
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public int ReconciliationInvocationCount =>
        Volatile.Read(ref _reconciliationInvocationCount);

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

    public Task<PaymentProviderReconciliationResult> ReconcileAsync(
        PaymentProviderReconciliationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Provider idempotency key is required.",
                nameof(request));
        }

        Interlocked.Increment(ref _reconciliationInvocationCount);

        if (!_authorisations.TryGetValue(request.IdempotencyKey, out var stored))
        {
            return Task.FromResult(
                new PaymentProviderReconciliationResult(
                    PaymentProviderReconciliationOutcome.Unknown,
                    null));
        }

        if (stored.PaymentId != request.PaymentId ||
            stored.OrderId != request.OrderId)
        {
            throw new InvalidOperationException(
                "Provider reconciliation identity does not match the stored authorisation.");
        }

        var result = _reconciliationScenario switch
        {
            SimulatedPaymentReconciliationScenario.Authorise =>
                new PaymentProviderReconciliationResult(
                    PaymentProviderReconciliationOutcome.Authorised,
                    stored.ProviderReference),
            SimulatedPaymentReconciliationScenario.Decline =>
                new PaymentProviderReconciliationResult(
                    PaymentProviderReconciliationOutcome.Declined,
                    stored.ProviderReference),
            SimulatedPaymentReconciliationScenario.StillIndeterminate =>
                new PaymentProviderReconciliationResult(
                    PaymentProviderReconciliationOutcome.Unknown,
                    null),
            _ => throw new InvalidOperationException(
                "Payment reconciliation simulation reached an unsupported scenario.")
        };

        return Task.FromResult(result);
    }

    private static SimulatedPaymentReconciliationScenario DefaultReconciliationScenario(
        SimulatedPaymentAuthorisationScenario scenario)
    {
        return scenario switch
        {
            SimulatedPaymentAuthorisationScenario.Authorise =>
                SimulatedPaymentReconciliationScenario.Authorise,
            SimulatedPaymentAuthorisationScenario.Decline =>
                SimulatedPaymentReconciliationScenario.Decline,
            SimulatedPaymentAuthorisationScenario.Indeterminate =>
                SimulatedPaymentReconciliationScenario.StillIndeterminate,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private sealed record SimulatedAuthorisation(
        Guid PaymentId,
        Guid OrderId,
        decimal Amount,
        string Currency,
        string ProviderReference,
        SimulatedPaymentAuthorisationScenario Scenario);
}
