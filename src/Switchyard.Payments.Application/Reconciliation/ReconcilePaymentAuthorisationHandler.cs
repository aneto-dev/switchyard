using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Reconciliation;

public sealed class ReconcilePaymentAuthorisationHandler
{
    private readonly IPaymentAuthorisationReconciliationStore _store;
    private readonly IPaymentAuthorisationReconciliationProvider _provider;
    private readonly TimeProvider _timeProvider;

    public ReconcilePaymentAuthorisationHandler(
        IPaymentAuthorisationReconciliationStore store,
        IPaymentAuthorisationReconciliationProvider provider,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReconcilePaymentAuthorisationResult> HandleAsync(
        ReconcilePaymentAuthorisationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment authorisation request ID cannot be empty.",
                nameof(command));
        }

        var current = await _store.GetAsync(command.RequestId, cancellationToken)
            ?? throw new PaymentAuthorisationNotFoundException(command.RequestId);

        if (current.Status != PaymentAuthorisationStatus.Indeterminate)
        {
            return ToResult(current, replayed: true);
        }

        var providerResult = await _provider.ReconcileAsync(
            new PaymentProviderReconciliationRequest(
                current.ProviderIdempotencyKey,
                current.PaymentId,
                current.OrderId,
                current.ProviderReference),
            cancellationToken);

        var status = providerResult.Outcome switch
        {
            PaymentProviderReconciliationOutcome.Authorised =>
                PaymentAuthorisationStatus.Authorised,
            PaymentProviderReconciliationOutcome.Declined =>
                PaymentAuthorisationStatus.Declined,
            PaymentProviderReconciliationOutcome.Unknown =>
                PaymentAuthorisationStatus.Indeterminate,
            _ => throw new InvalidOperationException(
                "Payment provider returned an unsupported reconciliation outcome.")
        };

        var providerReference = string.IsNullOrWhiteSpace(providerResult.ProviderReference)
            ? current.ProviderReference
            : providerResult.ProviderReference.Trim();

        if (status is PaymentAuthorisationStatus.Authorised or PaymentAuthorisationStatus.Declined &&
            providerReference is null)
        {
            throw new InvalidOperationException(
                "A definite provider reconciliation outcome requires a provider reference.");
        }

        var decision = await _store.ApplyAsync(
            new PaymentAuthorisationReconciliation(
                command.RequestId,
                status,
                providerReference,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        return ToResult(decision.State, replayed: !decision.Applied);
    }

    private static ReconcilePaymentAuthorisationResult ToResult(
        PaymentAuthorisationReconciliationState state, bool replayed)
    {
        return new ReconcilePaymentAuthorisationResult(
            state.RequestId,
            state.PaymentId,
            state.OrderId,
            state.Status,
            state.ProviderReference,
            state.ReconciliationAttemptCount,
            state.LastReconciledAtUtc,
            replayed);
    }
}
