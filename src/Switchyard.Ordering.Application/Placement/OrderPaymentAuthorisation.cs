using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

// Ordering's local record of the latest authorisation outcome Payments reported for an
// order's placement. It is a projection of Payments facts, kept so operators can see why a
// placement is waiting, failed or needs attention without querying Payments.
public sealed class OrderPaymentAuthorisation
{
    private OrderPaymentAuthorisation(
        OrderId orderId,
        Guid requestId,
        Guid? paymentId,
        PaymentAuthorisationOutcome outcome,
        decimal amount,
        string currency,
        string? providerReference,
        string? reason,
        int? reconciliationAttemptCount,
        DateTimeOffset outcomeAtUtc,
        DateTimeOffset recordedAtUtc)
    {
        OrderId = orderId;
        RequestId = requestId;
        PaymentId = paymentId;
        Outcome = outcome;
        Amount = amount;
        Currency = currency;
        ProviderReference = providerReference;
        Reason = reason;
        ReconciliationAttemptCount = reconciliationAttemptCount;
        OutcomeAtUtc = outcomeAtUtc;
        RecordedAtUtc = recordedAtUtc;
    }

    public OrderId OrderId { get; }

    public Guid RequestId { get; }

    public Guid? PaymentId { get; private set; }

    public PaymentAuthorisationOutcome Outcome { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public string? ProviderReference { get; private set; }

    public string? Reason { get; private set; }

    public int? ReconciliationAttemptCount { get; private set; }

    public DateTimeOffset OutcomeAtUtc { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public bool IsTerminal => IsTerminalOutcome(Outcome);

    public static OrderPaymentAuthorisation Start(
        OrderId orderId, PaymentAuthorisationObservation observation, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(observation);

        return new OrderPaymentAuthorisation(
            orderId,
            observation.RequestId,
            observation.PaymentId,
            observation.Outcome,
            observation.Amount,
            observation.Currency,
            observation.ProviderReference,
            observation.Reason,
            observation.ReconciliationAttemptCount,
            observation.OccurredAtUtc,
            recordedAtUtc);
    }

    public static OrderPaymentAuthorisation Rehydrate(
        OrderId orderId,
        Guid requestId,
        Guid? paymentId,
        PaymentAuthorisationOutcome outcome,
        decimal amount,
        string currency,
        string? providerReference,
        string? reason,
        int? reconciliationAttemptCount,
        DateTimeOffset outcomeAtUtc,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        return new OrderPaymentAuthorisation(
            orderId, requestId, paymentId, outcome, amount, currency, providerReference, reason,
            reconciliationAttemptCount, outcomeAtUtc, recordedAtUtc);
    }

    // Returns false when the observation adds nothing: a replay of the current outcome, or a
    // late non-terminal outcome that Payments has already moved past.
    public bool Apply(PaymentAuthorisationObservation observation, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.RequestId != RequestId)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Payment outcome request '{observation.RequestId}' does not match recorded request '{RequestId}'.");
        }

        if (PaymentId is { } knownPaymentId &&
            observation.PaymentId is { } observedPaymentId &&
            knownPaymentId != observedPaymentId)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Payment outcome for payment '{observedPaymentId}' conflicts with recorded payment '{knownPaymentId}'.");
        }

        if (IsTerminal)
        {
            // Replays and stale non-terminal outcomes delivered out of order are expected.
            if (observation.Outcome == Outcome || !IsTerminalOutcome(observation.Outcome))
            {
                return false;
            }

            throw new OrderPlacementPaymentOutcomeException(
                $"Payment outcome '{observation.Outcome}' conflicts with recorded terminal outcome '{Outcome}'.");
        }

        // Non-terminal outcomes only move forward: Indeterminate -> NeedsAttention -> terminal.
        if (!IsTerminalOutcome(observation.Outcome) && observation.Outcome <= Outcome)
        {
            return false;
        }

        PaymentId = observation.PaymentId ?? PaymentId;
        Outcome = observation.Outcome;
        Amount = observation.Amount;
        Currency = observation.Currency;
        ProviderReference = observation.ProviderReference ?? ProviderReference;
        Reason = observation.Reason;
        ReconciliationAttemptCount = observation.ReconciliationAttemptCount ?? ReconciliationAttemptCount;
        OutcomeAtUtc = observation.OccurredAtUtc;
        RecordedAtUtc = recordedAtUtc;

        return true;
    }

    private static bool IsTerminalOutcome(PaymentAuthorisationOutcome outcome) =>
        outcome is PaymentAuthorisationOutcome.Authorised or PaymentAuthorisationOutcome.Failed;
}
