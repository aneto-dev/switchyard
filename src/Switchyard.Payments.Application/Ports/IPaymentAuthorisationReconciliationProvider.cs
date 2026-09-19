using Switchyard.Payments.Application.Reconciliation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentAuthorisationReconciliationProvider
{
    Task<PaymentProviderReconciliationResult> ReconcileAsync(
        PaymentProviderReconciliationRequest request, CancellationToken cancellationToken);
}
