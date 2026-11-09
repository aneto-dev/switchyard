namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentRefundNotAppliedV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    DateTimeOffset ResolvedAtUtc)
{
    public const string MessageType =
        "payments.event.refund-not-applied.v1";
}
