namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentCapturedV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    string ProviderReference,
    DateTimeOffset CapturedAtUtc)
{
    public const string MessageType =
        "payments.event.captured.v1";
}
