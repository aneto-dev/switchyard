using System.Text.Json;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;

namespace Switchyard.Fulfilment.Application.Shipments;

public sealed class DispatchShipmentHandler
{
    private readonly IFulfilmentRequestRepository _repository;
    private readonly IOutboxWriter _outboxWriter;

    public DispatchShipmentHandler(
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

    public async Task<DispatchShipmentResult> HandleAsync(
        DispatchShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CausationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Dispatch causation ID cannot be empty.",
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

        var decision =
            request.DispatchShipment(
                command.ShipmentId,
                Guid.NewGuid(),
                command.DispatchedAtUtc);

        if (decision.CaptureRequired)
        {
            var capture =
                new CapturePaymentV1(
                    decision.PaymentCaptureRequestId!.Value,
                    request.OrderId,
                    request.PaymentId);

            await _outboxWriter.AddAsync(
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    CapturePaymentV1.MessageType,
                    JsonSerializer.Serialize(
                        capture,
                        JsonSerializerOptions.Web),
                    command.DispatchedAtUtc,
                    request.OrderId,
                    command.CausationId),
                cancellationToken);
        }

        if (!decision.Replayed)
        {
            await _repository.SaveAsync(
                request,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new DispatchShipmentResult(
            decision.Replayed,
            decision.CaptureRequired,
            decision.PaymentCaptureRequestId);
    }
}

public sealed record DispatchShipmentCommand(
    Guid OrderId,
    Guid ShipmentId,
    Guid CausationId,
    DateTimeOffset DispatchedAtUtc);

public sealed record DispatchShipmentResult(
    bool Replayed,
    bool CaptureRequired,
    Guid? PaymentCaptureRequestId);
