namespace Switchyard.Payments.Infrastructure.Persistence;

internal sealed class PaymentInboxMessageRecord
{
    public string ConsumerName { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public Guid? ProcessingToken { get; set; }
    public DateTimeOffset? ProcessingLockedUntilUtc { get; set; }
}