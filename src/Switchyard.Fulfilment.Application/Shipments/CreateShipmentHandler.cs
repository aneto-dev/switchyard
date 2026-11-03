using Switchyard.Fulfilment.Application.Requests;

namespace Switchyard.Fulfilment.Application.Shipments;

public sealed class CreateShipmentHandler
{
    private readonly IFulfilmentRequestRepository _repository;

    public CreateShipmentHandler(IFulfilmentRequestRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateShipmentResult> HandleAsync(
        CreateShipmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.UnitIds);

        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);

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
            await _repository.SaveAsync(request, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new CreateShipmentResult(!created);
    }
}

public sealed record CreateShipmentCommand(
    Guid OrderId,
    Guid ShipmentId,
    IReadOnlyList<Guid> UnitIds,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateShipmentResult(bool Replayed);
