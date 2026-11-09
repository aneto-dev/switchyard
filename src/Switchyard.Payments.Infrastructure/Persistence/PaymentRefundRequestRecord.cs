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
    public DateTimeOffset RequestedAtUtc { get; set; }
}
