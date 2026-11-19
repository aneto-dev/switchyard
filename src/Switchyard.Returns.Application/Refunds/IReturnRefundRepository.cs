namespace Switchyard.Returns.Application.Refunds;

public interface IReturnRefundRepository
{
    Task CreatePendingAsync(
        ReturnRefundProjection refund,
        CancellationToken cancellationToken);

    Task<ApplyReturnRefundOutcomeResult> ApplyOutcomeAsync(
        ApplyReturnRefundOutcome outcome,
        CancellationToken cancellationToken);
}

public sealed record ApplyReturnRefundOutcome(
    Guid OrderId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    ReturnRefundStatus Status,
    string? ProviderReference,
    DateTimeOffset OccurredAtUtc);

public sealed record ApplyReturnRefundOutcomeResult(
    ReturnRefundStatus Status,
    bool Replayed);
