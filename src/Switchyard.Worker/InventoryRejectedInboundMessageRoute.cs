using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class InventoryRejectedInboundMessageRoute :
    IOrderingInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public InventoryRejectedInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        InventoryRejectedV1.MessageType;

    public async Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        InventoryRejectedV1 outcome;

        try
        {
            outcome =
                JsonSerializer.Deserialize<InventoryRejectedV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Inventory rejected payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Inventory rejected payload is invalid: {exception.Message}");
        }

        ValidateEnvelope(
            message,
            outcome);

        var handler =
            new OrderingInventoryOutcomeHandler(
                new EfOrderRepository(dbContext),
                new EfOrderPlacementProcessRepository(dbContext),
                new EfOrderingOutboxStore(dbContext),
                _timeProvider);

        try
        {
            await handler.HandleRejectedAsync(
                outcome,
                message.MessageId,
                cancellationToken);
        }
        catch (OrderPlacementInventoryOutcomeException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
    }

    private static void ValidateEnvelope(
        IntegrationMessageEnvelope message,
        InventoryRejectedV1 outcome)
    {
        if (outcome.RequestId == Guid.Empty ||
            outcome.OrderId == Guid.Empty ||
            outcome.OrderLineId == Guid.Empty ||
            string.IsNullOrWhiteSpace(outcome.SkuCode) ||
            outcome.Quantity <= 0 ||
            !string.Equals(
                outcome.Reason,
                InventoryRejectedV1.InsufficientStockReason,
                StringComparison.Ordinal))
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory rejected outcome contains invalid required fields.");
        }

        if (message.CorrelationId != outcome.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory rejected correlation ID must match the order ID.");
        }
    }
}
