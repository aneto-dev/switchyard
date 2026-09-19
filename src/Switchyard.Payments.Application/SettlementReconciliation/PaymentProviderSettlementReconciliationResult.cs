namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed record PaymentProviderSettlementReconciliationResult(
    PaymentProviderSettlementReconciliationOutcome Outcome,
    string? ProviderReference);
