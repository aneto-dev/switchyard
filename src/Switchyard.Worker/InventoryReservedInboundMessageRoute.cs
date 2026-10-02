using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class InventoryReservedInboundMessageRoute :
    IOrderingInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public InventoryReservedInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        InventoryReservedV1.MessageType;

    public async Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        InventoryReservedV1 outcome;

        try
        {
            outcome =
                JsonSerializer.Deserialize<InventoryReservedV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Inventory reserved payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Inventory reserved payload is invalid: {exception.Message}");
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
            await handler.HandleReservedAsync(
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
        InventoryReservedV1 outcome)
    {
        if (outcome.RequestId == Guid.Empty ||
            outcome.OrderId == Guid.Empty ||
            outcome.OrderLineId == Guid.Empty ||
            outcome.ReservationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(outcome.SkuCode) ||
            outcome.Quantity <= 0 ||
            outcome.ReservedAtUtc == default ||
            outcome.ExpiresAtUtc == default ||
            outcome.ExpiresAtUtc <= outcome.ReservedAtUtc)
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory reserved outcome contains invalid required fields.");
        }

        if (message.CorrelationId != outcome.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory reserved correlation ID must match the order ID.");
        }
    }
}
