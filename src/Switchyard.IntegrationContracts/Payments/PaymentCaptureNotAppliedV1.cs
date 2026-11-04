namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentCaptureNotAppliedV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    string? ProviderReference,
    DateTimeOffset ResolvedAtUtc)
{
    public const string MessageType =
        "payments.event.capture-not-applied.v1";
}
