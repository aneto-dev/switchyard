using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed record PaymentSettlementReconciliationState(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    PaymentSettlementAction Action,
    decimal Amount,
    string Currency,
    PaymentSettlementStatus Status,
    string ProviderIdempotencyKey,
    string? ProviderReference,
    string AuthorisationProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    int ReconciliationAttemptCount,
    DateTimeOffset? LastReconciledAtUtc);
