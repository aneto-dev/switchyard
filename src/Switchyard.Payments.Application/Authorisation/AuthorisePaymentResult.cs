using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Authorisation;

public sealed record AuthorisePaymentResult(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    PaymentAuthorisationStatus Status,
    string ProviderIdempotencyKey,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    bool Replayed);
