using System.Text.Json;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class PaymentAuthorisationIndeterminateInboundMessageRoute :
    IOrderingInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public PaymentAuthorisationIndeterminateInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        PaymentAuthorisationIndeterminateV1.MessageType;

    public async Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        PaymentAuthorisationIndeterminateV1 outcome;

        try
        {
            outcome =
                JsonSerializer.Deserialize<PaymentAuthorisationIndeterminateV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Payment authorisation indeterminate payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Payment authorisation indeterminate payload is invalid: {exception.Message}");
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
            await handler.HandleIndeterminateAsync(
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
        PaymentAuthorisationIndeterminateV1 outcome)
    {
        if (outcome.RequestId == Guid.Empty ||
            outcome.OrderId == Guid.Empty ||
            outcome.PaymentId == Guid.Empty ||
            !IsValidAmountAndCurrency(
                outcome.Amount,
                outcome.Currency) ||
            (outcome.ProviderReference is not null &&
             string.IsNullOrWhiteSpace(outcome.ProviderReference)) ||
            outcome.IndeterminateAtUtc == default)
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment authorisation indeterminate outcome contains invalid required fields.");
        }

        if (message.CorrelationId != outcome.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment authorisation indeterminate correlation ID must match the order ID.");
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
