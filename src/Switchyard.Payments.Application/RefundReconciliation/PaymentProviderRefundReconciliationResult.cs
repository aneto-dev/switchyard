namespace Switchyard.Payments.Application.RefundReconciliation;

public sealed record PaymentProviderRefundReconciliationResult(
    PaymentProviderRefundReconciliationOutcome Outcome,
    string? ProviderReference);
