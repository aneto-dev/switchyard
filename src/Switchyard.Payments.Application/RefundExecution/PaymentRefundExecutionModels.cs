using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Payments.Application.RefundExecution;

public sealed record PaymentRefundExecutionState(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    PaymentRefundStatus Status,
    string ProviderIdempotencyKey,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ProviderInvocationClaimedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    int ReconciliationAttemptCount,
    DateTimeOffset? LastReconciledAtUtc);

public sealed record PaymentRefundProviderInvocationClaim(
    PaymentRefundExecutionState State,
    bool Acquired);

public sealed record PaymentRefundExecutionCompletion(
    Guid RequestId,
    PaymentRefundStatus Status,
    string? ProviderReference,
    DateTimeOffset ResolvedAtUtc);

public sealed record PaymentRefundExecutionDecision(
    PaymentRefundExecutionState State,
    bool Applied);
