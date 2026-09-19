using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Authorisation;

public sealed record PaymentAuthorisationCompletion(
    Guid RequestId,
    PaymentAuthorisationStatus Status,
    string? ProviderReference,
    DateTimeOffset ResolvedAtUtc);
