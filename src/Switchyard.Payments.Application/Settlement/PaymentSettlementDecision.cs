using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed record PaymentSettlementDecision(
    PaymentSettlementAttempt Attempt,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string AuthorisationProviderReference,
    bool Replayed);
