using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Reconciliation;

public sealed record ReconcilePaymentAuthorisationResult(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    PaymentAuthorisationStatus Status,
    string? ProviderReference,
    int ReconciliationAttemptCount,
    DateTimeOffset? LastReconciledAtUtc,
    bool Replayed);
