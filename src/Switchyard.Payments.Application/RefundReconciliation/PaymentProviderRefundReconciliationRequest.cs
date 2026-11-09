namespace Switchyard.Payments.Application.RefundReconciliation;

public sealed record PaymentProviderRefundReconciliationRequest(
    string IdempotencyKey,
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string? ProviderReference);
