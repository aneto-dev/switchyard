namespace Switchyard.Payments.Application.Authorisation;

public sealed record PaymentProviderAuthorisationRequest(
    string IdempotencyKey,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency);
