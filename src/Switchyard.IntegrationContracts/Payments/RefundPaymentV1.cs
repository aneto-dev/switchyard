namespace Switchyard.IntegrationContracts.Payments;

public sealed record RefundPaymentV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency)
{
    public const string MessageType =
        "payments.command.refund.v1";
}
