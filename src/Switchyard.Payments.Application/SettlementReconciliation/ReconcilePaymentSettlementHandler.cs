using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed class ReconcilePaymentSettlementHandler
{
    private readonly IPaymentSettlementReconciliationStore _store;
    private readonly IPaymentSettlementReconciliationProvider _provider;
    private readonly TimeProvider _timeProvider;

    public ReconcilePaymentSettlementHandler(
        IPaymentSettlementReconciliationStore store,
        IPaymentSettlementReconciliationProvider provider,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReconcilePaymentSettlementResult> HandleAsync(
        ReconcilePaymentSettlementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment settlement request ID cannot be empty.",
                nameof(command));
        }

        var current = await _store.GetAsync(command.RequestId, cancellationToken)
            ?? throw new PaymentSettlementReconciliationNotFoundException(command.RequestId);

        if (current.Status == PaymentSettlementStatus.Pending)
        {
            throw new InvalidOperationException(
                "Pending payment settlement must be retried through settlement execution before reconciliation.");
        }

        if (current.Status != PaymentSettlementStatus.Indeterminate)
        {
            return ToResult(current, replayed: true);
        }

        var providerResult = await _provider.ReconcileAsync(
            new PaymentProviderSettlementReconciliationRequest(
                current.ProviderIdempotencyKey,
                current.PaymentId,
                current.OrderId,
                current.Action,
                current.Amount,
                current.Currency,
                current.AuthorisationProviderReference,
                current.ProviderReference),
            cancellationToken);

        var status = providerResult.Outcome switch
        {
            PaymentProviderSettlementReconciliationOutcome.Succeeded =>
                PaymentSettlementStatus.Succeeded,
            PaymentProviderSettlementReconciliationOutcome.NotApplied =>
                PaymentSettlementStatus.NotApplied,
            PaymentProviderSettlementReconciliationOutcome.Unknown =>
                PaymentSettlementStatus.Indeterminate,
            _ => throw new InvalidOperationException(
                "Payment provider returned an unsupported settlement reconciliation outcome.")
        };

        var providerReference = string.IsNullOrWhiteSpace(providerResult.ProviderReference)
            ? current.ProviderReference
            : providerResult.ProviderReference.Trim();

        if (status == PaymentSettlementStatus.Succeeded &&
            string.IsNullOrWhiteSpace(providerReference))
        {
            throw new InvalidOperationException(
                "A successful provider settlement reconciliation requires a provider reference.");
        }

        var decision = await _store.ApplyAsync(
            new PaymentSettlementReconciliation(
                command.RequestId,
                status,
                providerReference,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        return ToResult(decision.State, replayed: !decision.Applied);
    }

    private static ReconcilePaymentSettlementResult ToResult(
        PaymentSettlementReconciliationState state,
        bool replayed)
    {
        return new ReconcilePaymentSettlementResult(
            state.RequestId,
            state.PaymentId,
            state.OrderId,
            state.Action,
            state.Status,
            state.ProviderReference,
            state.ReconciliationAttemptCount,
            state.LastReconciledAtUtc,
            replayed);
    }
}
