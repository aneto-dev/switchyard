using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed record ExecutePaymentSettlementResult(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    PaymentSettlementAction Action,
    PaymentSettlementStatus Status,
    string ProviderIdempotencyKey,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    bool Replayed);
