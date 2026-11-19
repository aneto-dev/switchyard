using Switchyard.Ordering.Application.Placement;

namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OrderPaymentAuthorisationRecord
{
    public Guid OrderId { get; set; }
    public Guid RequestId { get; set; }
    public Guid? PaymentId { get; set; }
    public PaymentAuthorisationOutcome Outcome { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string? Reason { get; set; }
    public int? ReconciliationAttemptCount { get; set; }
    public DateTimeOffset OutcomeAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
