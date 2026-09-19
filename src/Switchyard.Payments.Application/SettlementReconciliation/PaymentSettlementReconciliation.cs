using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed record PaymentSettlementReconciliation(
    Guid RequestId,
    PaymentSettlementStatus Status,
    string? ProviderReference,
    DateTimeOffset ReconciledAtUtc);
