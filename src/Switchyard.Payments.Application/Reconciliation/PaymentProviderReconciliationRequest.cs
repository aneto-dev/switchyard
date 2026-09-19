namespace Switchyard.Payments.Application.Reconciliation;

public sealed record PaymentProviderReconciliationRequest(
    string IdempotencyKey,
    Guid PaymentId,
    Guid OrderId,
    string? ProviderReference);
