namespace Switchyard.Payments.Application.Reconciliation;

public sealed record PaymentAuthorisationReconciliationDecision(
    PaymentAuthorisationReconciliationState State,
    bool Applied);
