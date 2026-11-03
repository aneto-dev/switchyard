namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentAuthorisationFailedV1(
    Guid RequestId,
    Guid OrderId,
    Guid? PaymentId,
    decimal Amount,
    string Currency,
    string Reason,
    string? ProviderReference,
    DateTimeOffset FailedAtUtc,
    DateTimeOffset? ReservationValidUntilUtc)
{
    public const string MessageType = "payments.event.authorisation-failed.v1";

    public const string DeclinedReason = "Declined";
    public const string ReservationExpiredReason = "ReservationExpired";
    public const string ReservationValidityMissingReason = "ReservationValidityMissing";
}
