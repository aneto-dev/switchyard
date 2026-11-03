using System.Text.Json;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.IntegrationContracts.Ordering;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed class OrderConfirmedFulfilmentInboundMessageRoute :
    IFulfilmentInboundMessageRoute
{
    public string MessageType =>
        OrderConfirmedV1.MessageType;

    public async Task HandleAsync(
        FulfilmentDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        OrderConfirmedV1 confirmed;

        try
        {
            confirmed =
                JsonSerializer.Deserialize<OrderConfirmedV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Order confirmed payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Order confirmed payload is invalid: {exception.Message}");
        }

        Validate(
            message,
            confirmed);

        var handler =
            new CreateFulfilmentHandler(
                new EfFulfilmentRequestRepository(
                    dbContext));

        try
        {
            await handler.HandleAsync(
                new CreateFulfilmentCommand(
                    confirmed.OrderId,
                    confirmed.PaymentId,
                    confirmed.OrderNumber,
                    confirmed.Lines.Select(
                        line =>
                            new CreateFulfilmentLine(
                                line.OrderLineId,
                                line.SkuCode,
                                line.ProductName,
                                line.Quantity))
                                   .ToArray(),
                    confirmed.ConfirmedAtUtc),
                cancellationToken);
        }
        catch (FulfilmentRequestConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
    }

    private static void Validate(
        IntegrationMessageEnvelope message,
        OrderConfirmedV1 confirmed)
    {
        if (confirmed.OrderId == Guid.Empty ||
            confirmed.PaymentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                confirmed.OrderNumber) ||
            confirmed.TotalAmount < 0 ||
            string.IsNullOrWhiteSpace(
                confirmed.Currency) ||
            confirmed.ConfirmedAtUtc ==
                default ||
            confirmed.Lines is null ||
            confirmed.Lines.Count == 0)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order confirmed event contains invalid required fields.");
        }

        if (message.CorrelationId !=
            confirmed.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order confirmed correlation ID must match the order ID.");
        }

        if (message.OccurredAtUtc !=
            confirmed.ConfirmedAtUtc)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order confirmed occurrence time must match the confirmation time.");
        }

        if (confirmed.Lines.Any(
                line =>
                    line.OrderLineId ==
                        Guid.Empty ||
                    string.IsNullOrWhiteSpace(
                        line.SkuCode) ||
                    string.IsNullOrWhiteSpace(
                        line.ProductName) ||
                    line.Quantity <= 0) ||
            confirmed.Lines
                     .GroupBy(
                         line =>
                             line.OrderLineId)
                     .Any(
                         group =>
                             group.Count() >
                             1))
        {
            throw new NonRetryableIntegrationMessageException(
                "Order confirmed event contains invalid line details.");
        }
    }
}
