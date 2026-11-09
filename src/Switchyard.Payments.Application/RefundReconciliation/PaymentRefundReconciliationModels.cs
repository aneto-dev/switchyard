using Switchyard.Payments.Application.RefundExecution;
using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Payments.Application.RefundReconciliation;

public sealed record PaymentRefundReconciliation(
    Guid RequestId,
    PaymentRefundStatus Status,
    string? ProviderReference,
    DateTimeOffset ReconciledAtUtc,
    int ExpectedReconciliationAttemptCount);

public sealed record PaymentRefundReconciliationDecision(
    PaymentRefundExecutionState State,
    bool Applied);
