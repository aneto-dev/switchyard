using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Infrastructure.Persistence;

internal sealed class PaymentSettlementAttemptRecord
{
    public Guid RequestId { get; set; }

    public Guid PaymentId { get; set; }

    public PaymentSettlementAction Action { get; set; }

    public PaymentSettlementStatus Status { get; set; }

    public string ProviderIdempotencyKey { get; set; } = string.Empty;

    public string? ProviderReference { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }
}
