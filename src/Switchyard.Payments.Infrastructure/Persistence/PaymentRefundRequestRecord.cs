using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Payments.Infrastructure.Persistence;

internal sealed class PaymentRefundRequestRecord
{
    public Guid RequestId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ReturnRequestId { get; set; }
    public Guid ReturnReceiptId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentRefundStatus Status { get; set; }
    public string ProviderIdempotencyKey { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? ProviderInvocationClaimedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public int ReconciliationAttemptCount { get; set; }
    public DateTimeOffset? LastReconciledAtUtc { get; set; }
}
