using Switchyard.Payments.Application.RefundReconciliation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentRefundReconciliationProvider
{
    Task<PaymentProviderRefundReconciliationResult> ReconcileAsync(
        PaymentProviderRefundReconciliationRequest request,
        CancellationToken cancellationToken);
}
