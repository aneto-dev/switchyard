using System.Text.Json;
using Switchyard.IntegrationContracts.Fulfilment;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Eligibility;
using Switchyard.Returns.Domain.Eligibility;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class ShipmentDeliveredReturnsInboundMessageRoute :
    IReturnsInboundMessageRoute
{
    public string MessageType =>
        ShipmentDeliveredV1.MessageType;

    public async Task HandleAsync(
        ReturnsDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ShipmentDeliveredV1 delivered;

        try
        {
            delivered =
                JsonSerializer.Deserialize<ShipmentDeliveredV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Shipment delivered payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Shipment delivered payload is invalid: {exception.Message}");
        }

        if (delivered.OrderId == Guid.Empty ||
            delivered.ShipmentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(delivered.OrderNumber) ||
            delivered.DeliveredAtUtc == default ||
            delivered.Lines.Count == 0 ||
            delivered.Lines.Any(
                line =>
                    line.OrderLineId == Guid.Empty ||
                    line.Quantity <= 0))
        {
            throw new NonRetryableIntegrationMessageException(
                "Shipment delivered outcome contains invalid required fields.");
        }

        if (message.CorrelationId != delivered.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Shipment delivered correlation ID must match the order ID.");
        }

        var handler =
            new ApplyShipmentDeliveredHandler(
                new EfReturnEligibilityRepository(dbContext));

        try
        {
            await handler.HandleAsync(
                new ApplyShipmentDeliveredCommand(
                    delivered.OrderId,
                    delivered.ShipmentId,
                    delivered.OrderNumber,
                    delivered.Lines.Select(
                        line =>
                            new ApplyShipmentDeliveredLine(
                                line.OrderLineId,
                                line.Quantity))
                                   .ToArray(),
                    delivered.DeliveredAtUtc),
                cancellationToken);
        }
        catch (ReturnEligibilityConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
    }
}
