namespace Switchyard.IntegrationContracts.Payments;

public sealed record CapturePaymentV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId)
{
    public const string MessageType = "payments.command.capture.v1";
}
