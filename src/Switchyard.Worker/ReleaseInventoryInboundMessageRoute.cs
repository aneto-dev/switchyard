using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed class ReleaseInventoryInboundMessageRoute :
    IInventoryInboundMessageRoute
{
    private readonly TimeProvider _timeProvider;

    public ReleaseInventoryInboundMessageRoute(
        TimeProvider timeProvider)
    {
        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public string MessageType =>
        ReleaseInventoryReservationV1.MessageType;

    public async Task HandleAsync(
        InventoryDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        ReleaseInventoryReservationV1 command;

        try
        {
            command =
                JsonSerializer.Deserialize<ReleaseInventoryReservationV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Release inventory payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Release inventory payload is invalid: {exception.Message}");
        }

        ValidateEnvelope(
            message,
            command);

        var handler =
            new ReleaseInventoryHandler(
                new EfInventoryReservationLifecycleStore(
                    dbContext),
                _timeProvider);

        ReleaseInventoryResult result;

        try
        {
            result =
                await handler.HandleAsync(
                    new ReleaseInventoryCommand(
                        command.RequestId,
                        command.OrderId,
                        command.ReservationId,
                        StockReservationReleaseReason.Compensation),
                    cancellationToken);
        }
        catch (InventoryReservationReleaseConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }

        var outgoing =
            result.Outcome switch
            {
                ReleaseInventoryOutcome.Released or
                ReleaseInventoryOutcome.AlreadyReleased
                    when result.ReleasedAtUtc.HasValue &&
                         result.AppliedReleaseReason.HasValue =>
                    new IntegrationMessageEnvelope(
                        Guid.NewGuid(),
                        InventoryReleasedV1.MessageType,
                        JsonSerializer.Serialize(
                            new InventoryReleasedV1(
                                result.RequestId,
                                result.OrderId,
                                result.ReservationId,
                                ToContractReason(
                                    result.AppliedReleaseReason.Value),
                                result.ReleasedAtUtc.Value),
                            JsonSerializerOptions.Web),
                        result.ReleasedAtUtc.Value,
                        command.OrderId,
                        message.MessageId),

                ReleaseInventoryOutcome.AlreadyExpired
                    when result.ExpiredAtUtc.HasValue =>
                    new IntegrationMessageEnvelope(
                        Guid.NewGuid(),
                        InventoryExpiredV1.MessageType,
                        JsonSerializer.Serialize(
                            new InventoryExpiredV1(
                                result.RequestId,
                                result.OrderId,
                                result.ReservationId,
                                result.ExpiredAtUtc.Value),
                            JsonSerializerOptions.Web),
                        result.ExpiredAtUtc.Value,
                        command.OrderId,
                        message.MessageId),

                ReleaseInventoryOutcome.NotFound =>
                    throw new NonRetryableIntegrationMessageException(
                        $"Inventory reservation '{command.ReservationId}' does not exist."),

                _ =>
                    throw new InvalidOperationException(
                        "Inventory release returned an invalid messaging outcome.")
            };

        await new EfInventoryOutboxStore(dbContext)
            .AddAsync(
                outgoing,
                cancellationToken);
    }

    private static void ValidateEnvelope(
        IntegrationMessageEnvelope message,
        ReleaseInventoryReservationV1 command)
    {
        if (command.RequestId == Guid.Empty ||
            command.OrderId == Guid.Empty ||
            command.ReservationId == Guid.Empty ||
            !string.Equals(
                command.Reason,
                ReleaseInventoryReservationV1.CompensationReason,
                StringComparison.Ordinal))
        {
            throw new NonRetryableIntegrationMessageException(
                "Release inventory command contains invalid required fields.");
        }

        if (message.CorrelationId != command.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Release inventory correlation ID must match the order ID.");
        }
    }

    private static string ToContractReason(
        StockReservationReleaseReason reason)
    {
        return reason switch
        {
            StockReservationReleaseReason.Compensation =>
                InventoryReleasedV1.CompensationReason,

            StockReservationReleaseReason.Cancellation =>
                InventoryReleasedV1.CancellationReason,

            _ =>
                throw new InvalidOperationException(
                    $"Inventory release reason '{reason}' cannot be published.")
        };
    }
}
