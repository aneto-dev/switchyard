using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Reconciliation;

public sealed record PaymentAuthorisationReconciliation(
    Guid RequestId,
    PaymentAuthorisationStatus Status,
    string? ProviderReference,
    DateTimeOffset ReconciledAtUtc);
