using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class InventoryExpiredInboundMessageRoute :
    IOrderingInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public InventoryExpiredInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        InventoryExpiredV1.MessageType;

    public async Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        InventoryExpiredV1 outcome;

        try
        {
            outcome =
                JsonSerializer.Deserialize<InventoryExpiredV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Inventory expired payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Inventory expired payload is invalid: {exception.Message}");
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
            await handler.HandleExpiredAsync(
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
        InventoryExpiredV1 outcome)
    {
        if (outcome.RequestId == Guid.Empty ||
            outcome.OrderId == Guid.Empty ||
            outcome.ReservationId == Guid.Empty ||
            outcome.ExpiredAtUtc == default)
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory expired outcome contains invalid required fields.");
        }

        if (message.CorrelationId != outcome.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory expired correlation ID must match the order ID.");
        }
    }
}
