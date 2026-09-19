using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;

namespace Switchyard.Payments.Application.Authorisation;

public sealed record PaymentAuthorisationDecision(
    PaymentIntent Intent,
    PaymentAuthorisationAttempt Attempt,
    bool Replayed);
