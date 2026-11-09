namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentRefundIndeterminateV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    DateTimeOffset DetectedAtUtc)
{
    public const string MessageType =
        "payments.event.refund-indeterminate.v1";
}
