using System.Text.Json;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class PaymentAuthorisationFailedInboundMessageRoute :
    IOrderingInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public PaymentAuthorisationFailedInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        PaymentAuthorisationFailedV1.MessageType;

    public async Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        PaymentAuthorisationFailedV1 outcome;

        try
        {
            outcome =
                JsonSerializer.Deserialize<PaymentAuthorisationFailedV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Payment authorisation failed payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Payment authorisation failed payload is invalid: {exception.Message}");
        }

        ValidateEnvelope(
            message,
            outcome);

        var handler =
            new OrderingPaymentOutcomeHandler(
                new EfOrderRepository(dbContext),
                new EfOrderPlacementProcessRepository(dbContext),
                new EfOrderingOutboxStore(dbContext),
                _timeProvider);

        try
        {
            await handler.HandleFailedAsync(
                outcome,
                message.MessageId,
                cancellationToken);
        }
        catch (OrderPlacementPaymentOutcomeException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
    }

    private static void ValidateEnvelope(
        IntegrationMessageEnvelope message,
        PaymentAuthorisationFailedV1 outcome)
    {
        var baseFieldsValid =
            outcome.RequestId != Guid.Empty &&
            outcome.OrderId != Guid.Empty &&
            IsValidAmountAndCurrency(
                outcome.Amount,
                outcome.Currency) &&
            outcome.FailedAtUtc != default;

        var declinedShape =
            string.Equals(
                outcome.Reason,
                PaymentAuthorisationFailedV1.DeclinedReason,
                StringComparison.Ordinal) &&
            outcome.PaymentId.HasValue &&
            outcome.PaymentId.Value != Guid.Empty &&
            !string.IsNullOrWhiteSpace(outcome.ProviderReference);

        var expiredShape =
            string.Equals(
                outcome.Reason,
                PaymentAuthorisationFailedV1.ReservationExpiredReason,
                StringComparison.Ordinal) &&
            !outcome.PaymentId.HasValue &&
            outcome.ProviderReference is null &&
            outcome.ReservationValidUntilUtc.HasValue;

        var missingValidityShape =
            string.Equals(
                outcome.Reason,
                PaymentAuthorisationFailedV1.ReservationValidityMissingReason,
                StringComparison.Ordinal) &&
            !outcome.PaymentId.HasValue &&
            outcome.ProviderReference is null &&
            !outcome.ReservationValidUntilUtc.HasValue;

        if (!baseFieldsValid ||
            (!declinedShape &&
             !expiredShape &&
             !missingValidityShape))
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment authorisation failed outcome contains invalid required fields.");
        }

        if (message.CorrelationId != outcome.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment authorisation failed correlation ID must match the order ID.");
        }
    }

    private static bool IsValidAmountAndCurrency(
        decimal amount,
        string currency) =>
        amount > 0m &&
        decimal.Round(
            amount,
            2,
            MidpointRounding.ToEven) == amount &&
        !string.IsNullOrWhiteSpace(currency) &&
        currency.Length == 3 &&
        currency.All(
            character =>
                character is >= 'A' and <= 'Z');
}
