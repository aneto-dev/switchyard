namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentRefundedV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    string ProviderReference,
    DateTimeOffset RefundedAtUtc)
{
    public const string MessageType =
        "payments.event.refunded.v1";
}
