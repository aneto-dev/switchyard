using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed record ReconcilePaymentSettlementResult(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    PaymentSettlementAction Action,
    PaymentSettlementStatus Status,
    string? ProviderReference,
    int ReconciliationAttemptCount,
    DateTimeOffset? LastReconciledAtUtc,
    bool Replayed);
