using Switchyard.Payments.Domain.Payments;

namespace Switchyard.Payments.Domain.Authorisation;

public sealed record PaymentAuthorisationAttempt
{
    public PaymentAuthorisationAttempt(
        Guid requestId, PaymentId paymentId, string providerIdempotencyKey,
        string? providerReference, PaymentAuthorisationStatus status,
        DateTimeOffset requestedAtUtc, DateTimeOffset? resolvedAtUtc)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Payment authorisation request ID cannot be empty.", nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(paymentId);

        if (string.IsNullOrWhiteSpace(providerIdempotencyKey))
        {
            throw new ArgumentException(
                "Provider idempotency key is required.",
                nameof(providerIdempotencyKey));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status), status,
                "Payment authorisation status is not supported.");
        }

        var normalizedProviderReference = string.IsNullOrWhiteSpace(providerReference)
            ? null
            : providerReference.Trim();

        if (status == PaymentAuthorisationStatus.AuthorisationPending &&
            (normalizedProviderReference is not null || resolvedAtUtc is not null))
        {
            throw new ArgumentException(
                "A pending payment authorisation cannot have a provider reference or resolved time.",
                nameof(status));
        }

        if (status is PaymentAuthorisationStatus.Authorised or PaymentAuthorisationStatus.Declined &&
            (normalizedProviderReference is null || resolvedAtUtc is null))
        {
            throw new ArgumentException(
                "A definite payment authorisation outcome requires a provider reference and resolved time.",
                nameof(status));
        }

        if (status == PaymentAuthorisationStatus.Indeterminate && resolvedAtUtc is null)
        {
            throw new ArgumentException(
                "An indeterminate payment authorisation must record when the unknown outcome was observed.",
                nameof(resolvedAtUtc));
        }

        if (resolvedAtUtc < requestedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedAtUtc),
                "Payment authorisation cannot resolve before it was requested.");
        }

        RequestId = requestId;
        PaymentId = paymentId;
        ProviderIdempotencyKey = providerIdempotencyKey.Trim();
        ProviderReference = normalizedProviderReference;
        Status = status;
        RequestedAtUtc = requestedAtUtc;
        ResolvedAtUtc = resolvedAtUtc;
    }

    public Guid RequestId { get; }

    public PaymentId PaymentId { get; }

    public string ProviderIdempotencyKey { get; }

    public string? ProviderReference { get; }

    public PaymentAuthorisationStatus Status { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    public DateTimeOffset? ResolvedAtUtc { get; }
}
