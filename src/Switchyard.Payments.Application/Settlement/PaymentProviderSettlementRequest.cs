using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed record PaymentProviderSettlementRequest(
    string IdempotencyKey,
    Guid PaymentId,
    Guid OrderId,
    PaymentSettlementAction Action,
    decimal Amount,
    string Currency,
    string AuthorisationProviderReference);
