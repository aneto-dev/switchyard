using System.Text.Json;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.IntegrationContracts.Fulfilment;
using Switchyard.Messaging;

namespace Switchyard.Fulfilment.Application.Shipments;

public sealed class CreateShipmentHandler
{
    private readonly IFulfilmentRequestRepository _repository;
    private readonly IOutboxWriter _outboxWriter;

    public CreateShipmentHandler(
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

    public async Task<CreateShipmentResult> HandleAsync(
        CreateShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.UnitIds);

        if (command.CausationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Shipment creation causation ID cannot be empty.",
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

        var created =
            request.CreateShipment(
                command.ShipmentId,
                command.UnitIds,
                command.CreatedAtUtc);

        if (created)
        {
            var shipmentCreated =
                new ShipmentCreatedV1(
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
                    command.CreatedAtUtc);

            await _outboxWriter.AddAsync(
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    ShipmentCreatedV1.MessageType,
                    JsonSerializer.Serialize(
                        shipmentCreated,
                        JsonSerializerOptions.Web),
                    command.CreatedAtUtc,
                    request.OrderId,
                    command.CausationId),
                cancellationToken);

            await _repository.SaveAsync(
                request,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new CreateShipmentResult(
            Replayed: !created);
    }
}

public sealed record CreateShipmentCommand(
    Guid OrderId,
    Guid ShipmentId,
    IReadOnlyList<Guid> UnitIds,
    Guid CausationId,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateShipmentResult(bool Replayed);
