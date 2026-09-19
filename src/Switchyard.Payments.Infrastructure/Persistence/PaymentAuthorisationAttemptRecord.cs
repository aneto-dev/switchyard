using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Infrastructure.Persistence;

internal sealed class PaymentAuthorisationAttemptRecord
{
    public Guid RequestId { get; set; }

    public Guid PaymentId { get; set; }

    public string ProviderIdempotencyKey { get; set; } = string.Empty;

    public string? ProviderReference { get; set; }

    public PaymentAuthorisationStatus Status { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }
}
