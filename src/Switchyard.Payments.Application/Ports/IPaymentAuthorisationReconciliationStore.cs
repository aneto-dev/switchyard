using Switchyard.Payments.Application.Reconciliation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentAuthorisationReconciliationStore
{
    Task<PaymentAuthorisationReconciliationState?> GetAsync(
        Guid requestId, CancellationToken cancellationToken);

    Task<PaymentAuthorisationReconciliationDecision> ApplyAsync(
        PaymentAuthorisationReconciliation reconciliation, CancellationToken cancellationToken);
}
