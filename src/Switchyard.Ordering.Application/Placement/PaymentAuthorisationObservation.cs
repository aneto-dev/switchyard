namespace Switchyard.Ordering.Application.Placement;

// One authorisation outcome as reported by Payments. PaymentId is absent when Payments
// rejected the request before creating a payment, e.g. an expired reservation.
public sealed record PaymentAuthorisationObservation(
    Guid RequestId,
    Guid? PaymentId,
    PaymentAuthorisationOutcome Outcome,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    string? Reason,
    int? ReconciliationAttemptCount,
    DateTimeOffset OccurredAtUtc);
