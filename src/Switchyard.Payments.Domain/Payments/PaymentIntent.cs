using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Domain.Payments;

public sealed record PaymentIntent
{
    public PaymentIntent(
        PaymentId id, Guid orderId, PaymentAmount amount,
        PaymentAuthorisationStatus authorisationStatus,
        DateTimeOffset createdAtUtc, DateTimeOffset? authorisationResolvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(amount);

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(orderId));
        }

        if (!Enum.IsDefined(authorisationStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorisationStatus), authorisationStatus,
                "Payment authorisation status is not supported.");
        }

        if (authorisationStatus == PaymentAuthorisationStatus.AuthorisationPending &&
            authorisationResolvedAtUtc is not null)
        {
            throw new ArgumentException(
                "A pending payment authorisation cannot have a resolved time.",
                nameof(authorisationResolvedAtUtc));
        }

        if (authorisationStatus != PaymentAuthorisationStatus.AuthorisationPending &&
            authorisationResolvedAtUtc is null)
        {
            throw new ArgumentException(
                "A resolved payment authorisation must have a resolved time.",
                nameof(authorisationResolvedAtUtc));
        }

        if (authorisationResolvedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorisationResolvedAtUtc),
                "Payment authorisation cannot resolve before the payment intent was created.");
        }

        Id = id;
        OrderId = orderId;
        Amount = amount;
        AuthorisationStatus = authorisationStatus;
        CreatedAtUtc = createdAtUtc;
        AuthorisationResolvedAtUtc = authorisationResolvedAtUtc;
    }

    public PaymentId Id { get; }

    public Guid OrderId { get; }

    public PaymentAmount Amount { get; }

    public PaymentAuthorisationStatus AuthorisationStatus { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? AuthorisationResolvedAtUtc { get; }
}
