using System.Text.Json;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.IntegrationContracts.Fulfilment;
using Switchyard.Messaging;

namespace Switchyard.Fulfilment.Application.Shipments;

public sealed class DeliverShipmentHandler
{
    private readonly IFulfilmentRequestRepository _repository;
    private readonly IOutboxWriter _outboxWriter;

    public DeliverShipmentHandler(
        IFulfilmentRequestRepository repository,
        IOutboxWriter outboxWriter)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(nameof(repository));

        _outboxWriter =
            outboxWriter ??
            throw new ArgumentNullException(nameof(outboxWriter));
    }

    public async Task<DeliverShipmentResult> HandleAsync(
        DeliverShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CausationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Shipment delivery causation ID cannot be empty.",
                nameof(command));
        }

        await using var transaction =
            await _repository.BeginTransactionAsync(
                cancellationToken);

        var request =
            await _repository.GetByOrderIdForUpdateAsync(
                command.OrderId,
                cancellationToken) ??
            throw new InvalidOperationException(
                $"Fulfilment request '{command.OrderId}' was not found.");

        var delivered =
            request.DeliverShipment(
                command.ShipmentId,
                command.DeliveredAtUtc);

        if (delivered)
        {
            var shipmentDelivered =
                new ShipmentDeliveredV1(
                    request.OrderId,
                    command.ShipmentId,
                    request.OrderNumber,
                    request.GetShipmentLines(command.ShipmentId)
                           .Select(
                               line =>
                                   new ShipmentLineV1(
                                       line.OrderLineId,
                                       line.Quantity))
                           .ToArray(),
                    command.DeliveredAtUtc);

            await _outboxWriter.AddAsync(
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    ShipmentDeliveredV1.MessageType,
                    JsonSerializer.Serialize(
                        shipmentDelivered,
                        JsonSerializerOptions.Web),
                    command.DeliveredAtUtc,
                    request.OrderId,
                    command.CausationId),
                cancellationToken);

            await _repository.SaveAsync(
                request,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new DeliverShipmentResult(
            Replayed: !delivered);
    }
}

public sealed record DeliverShipmentCommand(
    Guid OrderId,
    Guid ShipmentId,
    Guid CausationId,
    DateTimeOffset DeliveredAtUtc);

public sealed record DeliverShipmentResult(
    bool Replayed);
