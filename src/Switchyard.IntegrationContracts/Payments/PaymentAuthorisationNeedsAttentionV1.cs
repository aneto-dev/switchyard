namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentAuthorisationNeedsAttentionV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    string Reason,
    int ReconciliationAttemptCount,
    DateTimeOffset ObservedAtUtc)
{
    public const string MessageType =
        "payments.event.authorisation-needs-attention.v1";

    public const string ReconciliationExhaustedReason =
        "ReconciliationExhausted";
}
