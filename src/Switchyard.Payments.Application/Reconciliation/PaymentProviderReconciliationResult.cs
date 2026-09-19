namespace Switchyard.Payments.Application.Reconciliation;

public sealed record PaymentProviderReconciliationResult(
    PaymentProviderReconciliationOutcome Outcome,
    string? ProviderReference);
