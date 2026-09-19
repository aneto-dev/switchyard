using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Infrastructure.Persistence;

internal sealed class PaymentIntentRecord
{
    public Guid PaymentId { get; set; }

    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public PaymentAuthorisationStatus AuthorisationStatus { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? AuthorisationResolvedAtUtc { get; set; }
}
