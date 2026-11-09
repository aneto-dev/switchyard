namespace Switchyard.Payments.Application.Refunds;

public sealed record PaymentProviderRefundRequest(
    string IdempotencyKey,
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency);
