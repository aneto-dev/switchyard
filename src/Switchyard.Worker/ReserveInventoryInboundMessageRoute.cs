using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed class ReserveInventoryInboundMessageRoute :
    IInventoryInboundMessageRoute
{
    private readonly InventoryReservationPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public ReserveInventoryInboundMessageRoute(
        InventoryReservationPolicy policy,
        TimeProvider timeProvider)
    {
        _policy =
            policy ??
            throw new ArgumentNullException(nameof(policy));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        ReserveInventoryV1.MessageType;

    public async Task HandleAsync(
        InventoryDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        ReserveInventoryV1 command;

        try
        {
            command =
                JsonSerializer.Deserialize<ReserveInventoryV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Reserve inventory payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Reserve inventory payload is invalid: {exception.Message}");
        }

        ValidateEnvelope(message, command);

        var handler =
            new ReserveInventoryHandler(
                new EfInventoryReservationStore(dbContext),
                _policy,
                _timeProvider);

        ReserveInventoryResult result;

        try
        {
            result =
                await handler.HandleAsync(
                    new ReserveInventoryCommand(
                        command.RequestId,
                        command.OrderId,
                        command.SkuCode,
                        command.Quantity),
                    cancellationToken);
        }
        catch (InventoryReservationConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }

        var outbox =
            new EfInventoryOutboxStore(dbContext);

        var occurredAtUtc =
            result.ReservedAtUtc ??
            _timeProvider.GetUtcNow();

        var outgoing =
            result.Outcome switch
            {
                InventoryReservationOutcome.Reserved
                    when result.ReservationId.HasValue &&
                         result.ReservedAtUtc.HasValue &&
                         result.ExpiresAtUtc.HasValue =>
                    new IntegrationMessageEnvelope(
                        Guid.NewGuid(),
                        InventoryReservedV1.MessageType,
                        JsonSerializer.Serialize(
                            new InventoryReservedV1(
                                command.RequestId,
                                command.OrderId,
                                command.OrderLineId,
                                result.SkuCode,
                                result.Quantity,
                                result.ReservationId.Value,
                                result.ReservedAtUtc.Value,
                                result.ExpiresAtUtc.Value),
                            JsonSerializerOptions.Web),
                        occurredAtUtc,
                        command.OrderId,
                        message.MessageId),

                InventoryReservationOutcome.InsufficientStock =>
                    new IntegrationMessageEnvelope(
                        Guid.NewGuid(),
                        InventoryRejectedV1.MessageType,
                        JsonSerializer.Serialize(
                            new InventoryRejectedV1(
                                command.RequestId,
                                command.OrderId,
                                command.OrderLineId,
                                result.SkuCode,
                                result.Quantity,
                                InventoryRejectedV1.InsufficientStockReason,
                                occurredAtUtc),
                            JsonSerializerOptions.Web),
                        occurredAtUtc,
                        command.OrderId,
                        message.MessageId),

                _ => throw new InvalidOperationException(
                    "Inventory reservation returned an invalid messaging outcome.")
            };

        await outbox.AddAsync(
            outgoing,
            cancellationToken);
    }

    private static void ValidateEnvelope(
        IntegrationMessageEnvelope message,
        ReserveInventoryV1 command)
    {
        if (command.RequestId == Guid.Empty ||
            command.OrderId == Guid.Empty ||
            command.OrderLineId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.SkuCode) ||
            command.Quantity <= 0)
        {
            throw new NonRetryableIntegrationMessageException(
                "Reserve inventory command contains invalid required fields.");
        }

        if (message.CorrelationId != command.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Reserve inventory correlation ID must match the order ID.");
        }
    }
}
