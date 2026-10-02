using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class InventoryReleasedInboundMessageRoute :
    IOrderingInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public InventoryReleasedInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        InventoryReleasedV1.MessageType;

    public async Task HandleAsync(
        OrderingDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        InventoryReleasedV1 outcome;

        try
        {
            outcome =
                JsonSerializer.Deserialize<InventoryReleasedV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Inventory released payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Inventory released payload is invalid: {exception.Message}");
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
            await handler.HandleReleasedAsync(
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
        InventoryReleasedV1 outcome)
    {
        if (outcome.RequestId == Guid.Empty ||
            outcome.OrderId == Guid.Empty ||
            outcome.ReservationId == Guid.Empty ||
            outcome.ReleasedAtUtc == default ||
            (!string.Equals(
                 outcome.Reason,
                 InventoryReleasedV1.CompensationReason,
                 StringComparison.Ordinal) &&
             !string.Equals(
                 outcome.Reason,
                 InventoryReleasedV1.CancellationReason,
                 StringComparison.Ordinal)))
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory released outcome contains invalid required fields.");
        }

        if (message.CorrelationId != outcome.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Inventory released correlation ID must match the order ID.");
        }
    }
}
