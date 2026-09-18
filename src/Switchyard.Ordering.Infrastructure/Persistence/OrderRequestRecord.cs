namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OrderRequestRecord
{
    public string IdempotencyKey { get; set; } = string.Empty;

    public string RequestFingerprint { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    public DateTimeOffset AcceptedAtUtc { get; set; }
}
