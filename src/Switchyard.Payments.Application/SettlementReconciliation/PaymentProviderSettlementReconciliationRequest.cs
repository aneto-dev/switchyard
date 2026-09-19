using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed record PaymentProviderSettlementReconciliationRequest(
    string IdempotencyKey,
    Guid PaymentId,
    Guid OrderId,
    PaymentSettlementAction Action,
    decimal Amount,
    string Currency,
    string AuthorisationProviderReference,
    string? ProviderReference);
