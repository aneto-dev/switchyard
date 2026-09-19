using Switchyard.Payments.Domain.Payments;

namespace Switchyard.Payments.Application.Authorisation;

public sealed record PaymentAuthorisationRequest(
    Guid RequestId,
    Guid OrderId,
    PaymentAmount Amount,
    DateTimeOffset RequestedAtUtc);
