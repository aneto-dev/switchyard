using Switchyard.Payments.Domain.Payments;

namespace Switchyard.Payments.Domain.Settlement;

public sealed record PaymentSettlementAttempt
{
    public PaymentSettlementAttempt(
        Guid requestId, PaymentId paymentId, PaymentSettlementAction action,
        PaymentSettlementStatus status, string providerIdempotencyKey,
        string? providerReference, DateTimeOffset requestedAtUtc,
        DateTimeOffset? resolvedAtUtc)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Payment settlement request ID cannot be empty.", nameof(requestId));
        }

        ArgumentNullException.ThrowIfNull(paymentId);

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(
                nameof(action), action,
                "Payment settlement action is not supported.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status), status,
                "Payment settlement status is not supported.");
        }

        if (string.IsNullOrWhiteSpace(providerIdempotencyKey))
        {
            throw new ArgumentException(
                "Provider idempotency key is required.",
                nameof(providerIdempotencyKey));
        }

        var normalizedProviderReference = string.IsNullOrWhiteSpace(providerReference)
            ? null
            : providerReference.Trim();

        if (status == PaymentSettlementStatus.Pending &&
            (normalizedProviderReference is not null || resolvedAtUtc is not null))
        {
            throw new ArgumentException(
                "A pending payment settlement cannot have a provider reference or resolved time.",
                nameof(status));
        }

        if (status == PaymentSettlementStatus.Succeeded &&
            (normalizedProviderReference is null || resolvedAtUtc is null))
        {
            throw new ArgumentException(
                "A successful payment settlement requires a provider reference and resolved time.",
                nameof(status));
        }

        if (status == PaymentSettlementStatus.Indeterminate && resolvedAtUtc is null)
        {
            throw new ArgumentException(
                "An indeterminate payment settlement must record when the unknown outcome was observed.",
                nameof(resolvedAtUtc));
        }

        if (resolvedAtUtc < requestedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedAtUtc),
                "Payment settlement cannot resolve before it was requested.");
        }

        RequestId = requestId;
        PaymentId = paymentId;
        Action = action;
        Status = status;
        ProviderIdempotencyKey = providerIdempotencyKey.Trim();
        ProviderReference = normalizedProviderReference;
        RequestedAtUtc = requestedAtUtc;
        ResolvedAtUtc = resolvedAtUtc;
    }

    public Guid RequestId { get; }

    public PaymentId PaymentId { get; }

    public PaymentSettlementAction Action { get; }

    public PaymentSettlementStatus Status { get; }

    public string ProviderIdempotencyKey { get; }

    public string? ProviderReference { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    public DateTimeOffset? ResolvedAtUtc { get; }
}
