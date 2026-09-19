using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Reconciliation;

public sealed record PaymentAuthorisationReconciliationState(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    PaymentAuthorisationStatus Status,
    string ProviderIdempotencyKey,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    int ReconciliationAttemptCount,
    DateTimeOffset? LastReconciledAtUtc);
