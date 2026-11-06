namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentCaptureIndeterminateV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    string? ProviderReference,
    DateTimeOffset ObservedAtUtc)
{
    public const string MessageType =
        "payments.event.capture-indeterminate.v1";
}
