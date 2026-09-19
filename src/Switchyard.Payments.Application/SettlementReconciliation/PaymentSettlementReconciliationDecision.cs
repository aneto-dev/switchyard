namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed record PaymentSettlementReconciliationDecision(
    PaymentSettlementReconciliationState State,
    bool Applied);
