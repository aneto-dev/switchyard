using Switchyard.Payments.Application.SettlementReconciliation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentSettlementReconciliationStore
{
    Task<PaymentSettlementReconciliationState?> GetAsync(
        Guid requestId, CancellationToken cancellationToken);

    Task<PaymentSettlementReconciliationDecision> ApplyAsync(
        PaymentSettlementReconciliation reconciliation,
        CancellationToken cancellationToken);
}
