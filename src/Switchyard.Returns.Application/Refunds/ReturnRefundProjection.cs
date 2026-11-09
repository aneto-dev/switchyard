namespace Switchyard.Returns.Application.Refunds;

public sealed record ReturnRefundProjection(
    Guid OrderId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    ReturnRefundStatus Status,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset UpdatedAtUtc);
