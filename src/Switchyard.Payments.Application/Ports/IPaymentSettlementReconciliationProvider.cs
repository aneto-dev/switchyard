using Switchyard.Payments.Application.SettlementReconciliation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentSettlementReconciliationProvider
{
    Task<PaymentProviderSettlementReconciliationResult> ReconcileAsync(
        PaymentProviderSettlementReconciliationRequest request,
        CancellationToken cancellationToken);
}
