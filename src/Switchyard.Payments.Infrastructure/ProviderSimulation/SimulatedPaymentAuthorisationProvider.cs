using System.Collections.Concurrent;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Reconciliation;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Application.SettlementReconciliation;
using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Infrastructure.ProviderSimulation;

public sealed class SimulatedPaymentAuthorisationProvider :
    IPaymentAuthorisationProvider,
    IPaymentAuthorisationReconciliationProvider,
    IPaymentSettlementProvider,
    IPaymentSettlementReconciliationProvider
{
    private readonly SimulatedPaymentAuthorisationScenario _scenario;
    private readonly SimulatedPaymentReconciliationScenario _reconciliationScenario;
    private readonly SimulatedPaymentSettlementScenario _settlementScenario;
    private readonly SimulatedPaymentSettlementReconciliationScenario _settlementReconciliationScenario;
    private readonly ConcurrentDictionary<string, SimulatedAuthorisation> _authorisations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SimulatedAuthorisation> _authorisationsByReference =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SimulatedSettlement> _settlements =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SimulatedSettlement> _settlementsByAuthorisation =
        new(StringComparer.Ordinal);
    private int _invocationCount;
    private int _reconciliationInvocationCount;
    private int _settlementInvocationCount;
    private int _settlementReconciliationInvocationCount;

    public SimulatedPaymentAuthorisationProvider(SimulatedPaymentAuthorisationScenario scenario)
        : this(
            scenario,
            DefaultReconciliationScenario(scenario),
            SimulatedPaymentSettlementScenario.Succeed,
            SimulatedPaymentSettlementReconciliationScenario.Succeed)
    {
    }

    public SimulatedPaymentAuthorisationProvider(
        SimulatedPaymentAuthorisationScenario scenario,
        SimulatedPaymentReconciliationScenario reconciliationScenario)
        : this(
            scenario,
            reconciliationScenario,
            SimulatedPaymentSettlementScenario.Succeed,
            SimulatedPaymentSettlementReconciliationScenario.Succeed)
    {
    }

    public SimulatedPaymentAuthorisationProvider(
        SimulatedPaymentAuthorisationScenario scenario,
        SimulatedPaymentReconciliationScenario reconciliationScenario,
        SimulatedPaymentSettlementScenario settlementScenario)
        : this(
            scenario,
            reconciliationScenario,
            settlementScenario,
            DefaultSettlementReconciliationScenario(settlementScenario))
    {
    }

    public SimulatedPaymentAuthorisationProvider(
        SimulatedPaymentAuthorisationScenario scenario,
        SimulatedPaymentReconciliationScenario reconciliationScenario,
        SimulatedPaymentSettlementScenario settlementScenario,
        SimulatedPaymentSettlementReconciliationScenario settlementReconciliationScenario)
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

        if (!Enum.IsDefined(settlementScenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settlementScenario), settlementScenario,
                "Payment settlement simulation scenario is not supported.");
        }

        if (!Enum.IsDefined(settlementReconciliationScenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settlementReconciliationScenario),
                settlementReconciliationScenario,
                "Payment settlement reconciliation simulation scenario is not supported.");
        }

        _scenario = scenario;
        _reconciliationScenario = reconciliationScenario;
        _settlementScenario = settlementScenario;
        _settlementReconciliationScenario = settlementReconciliationScenario;
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public int ReconciliationInvocationCount =>
        Volatile.Read(ref _reconciliationInvocationCount);

    public int SettlementInvocationCount =>
        Volatile.Read(ref _settlementInvocationCount);

    public int SettlementReconciliationInvocationCount =>
        Volatile.Read(ref _settlementReconciliationInvocationCount);

    public int UniqueAuthorisationCount => _authorisations.Count;

    public int UniqueSettlementCount => _settlements.Count;

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
        _authorisationsByReference.TryAdd(stored.ProviderReference, stored);

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

    public Task<PaymentProviderSettlementResult> ExecuteAsync(
        PaymentProviderSettlementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Provider idempotency key is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.AuthorisationProviderReference))
        {
            throw new ArgumentException(
                "Authorisation provider reference is required.",
                nameof(request));
        }

        Interlocked.Increment(ref _settlementInvocationCount);

        if (!_authorisationsByReference.TryGetValue(
            request.AuthorisationProviderReference,
            out var authorisation))
        {
            throw new InvalidOperationException(
                "Provider authorisation reference does not exist.");
        }

        if (authorisation.PaymentId != request.PaymentId ||
            authorisation.OrderId != request.OrderId ||
            authorisation.Amount != request.Amount ||
            !string.Equals(authorisation.Currency, request.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider settlement identity does not match the stored authorisation.");
        }

        var prefix = request.Action == PaymentSettlementAction.Capture
            ? "sim-capture"
            : "sim-void";

        var candidate = new SimulatedSettlement(
            request.IdempotencyKey,
            request.PaymentId,
            request.OrderId,
            request.Action,
            request.Amount,
            request.Currency,
            request.AuthorisationProviderReference,
            $"{prefix}-{Guid.NewGuid():N}");

        var financialAction = _settlementsByAuthorisation.GetOrAdd(
            request.AuthorisationProviderReference,
            candidate);

        if (!string.Equals(
                financialAction.IdempotencyKey,
                candidate.IdempotencyKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider authorisation has already been used by another settlement action.");
        }

        var stored = _settlements.GetOrAdd(request.IdempotencyKey, financialAction);

        if (stored.PaymentId != request.PaymentId ||
            stored.OrderId != request.OrderId ||
            stored.Action != request.Action ||
            stored.Amount != request.Amount ||
            !string.Equals(stored.Currency, request.Currency, StringComparison.Ordinal) ||
            !string.Equals(
                stored.AuthorisationProviderReference,
                request.AuthorisationProviderReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider settlement idempotency key was reused with different settlement data.");
        }

        if (_settlementScenario == SimulatedPaymentSettlementScenario.Indeterminate)
        {
            throw new PaymentSettlementProviderIndeterminateException();
        }

        return Task.FromResult(
            new PaymentProviderSettlementResult(stored.ProviderReference));
    }

    public Task<PaymentProviderSettlementReconciliationResult> ReconcileAsync(
        PaymentProviderSettlementReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Provider idempotency key is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.AuthorisationProviderReference))
        {
            throw new ArgumentException(
                "Authorisation provider reference is required.",
                nameof(request));
        }

        Interlocked.Increment(ref _settlementReconciliationInvocationCount);

        if (!_settlements.TryGetValue(request.IdempotencyKey, out var stored))
        {
            return Task.FromResult(
                new PaymentProviderSettlementReconciliationResult(
                    PaymentProviderSettlementReconciliationOutcome.NotApplied,
                    null));
        }

        if (stored.PaymentId != request.PaymentId ||
            stored.OrderId != request.OrderId ||
            stored.Action != request.Action ||
            stored.Amount != request.Amount ||
            !string.Equals(stored.Currency, request.Currency, StringComparison.Ordinal) ||
            !string.Equals(
                stored.AuthorisationProviderReference,
                request.AuthorisationProviderReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider settlement reconciliation identity does not match the stored settlement.");
        }

        var result = _settlementReconciliationScenario switch
        {
            SimulatedPaymentSettlementReconciliationScenario.Succeed =>
                new PaymentProviderSettlementReconciliationResult(
                    PaymentProviderSettlementReconciliationOutcome.Succeeded,
                    stored.ProviderReference),
            SimulatedPaymentSettlementReconciliationScenario.NotApplied =>
                new PaymentProviderSettlementReconciliationResult(
                    PaymentProviderSettlementReconciliationOutcome.NotApplied,
                    null),
            SimulatedPaymentSettlementReconciliationScenario.StillIndeterminate =>
                new PaymentProviderSettlementReconciliationResult(
                    PaymentProviderSettlementReconciliationOutcome.Unknown,
                    null),
            _ => throw new InvalidOperationException(
                "Payment settlement reconciliation simulation reached an unsupported scenario.")
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

    private static SimulatedPaymentSettlementReconciliationScenario DefaultSettlementReconciliationScenario(
        SimulatedPaymentSettlementScenario scenario)
    {
        return scenario switch
        {
            SimulatedPaymentSettlementScenario.Succeed =>
                SimulatedPaymentSettlementReconciliationScenario.Succeed,
            SimulatedPaymentSettlementScenario.Indeterminate =>
                SimulatedPaymentSettlementReconciliationScenario.StillIndeterminate,
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

    private sealed record SimulatedSettlement(
        string IdempotencyKey,
        Guid PaymentId,
        Guid OrderId,
        PaymentSettlementAction Action,
        decimal Amount,
        string Currency,
        string AuthorisationProviderReference,
        string ProviderReference);
}
