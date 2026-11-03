using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Application.Units;

public sealed class CreateFulfilmentUnitHandler
{
    private readonly IFulfilmentRequestRepository _repository;

    public CreateFulfilmentUnitHandler(
        IFulfilmentRequestRepository repository)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateFulfilmentUnitResult> HandleAsync(
        CreateFulfilmentUnitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        await using var transaction =
            await _repository.BeginTransactionAsync(
                cancellationToken);

        var request =
            await _repository.GetByOrderIdForUpdateAsync(
                command.OrderId,
                cancellationToken) ??
            throw new FulfilmentRequestNotFoundException(
                command.OrderId);

        var created =
            request.CreateUnit(
                command.UnitId,
                command.Lines.Select(
                    line =>
                        new FulfilmentUnitLine(
                            line.OrderLineId,
                            line.Quantity)),
                command.CreatedAtUtc);

        if (created)
        {
            await _repository.SaveAsync(
                request,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new CreateFulfilmentUnitResult(
            Replayed: !created,
            request.Status);
    }
}

public sealed record CreateFulfilmentUnitCommand(
    Guid OrderId,
    Guid UnitId,
    IReadOnlyList<CreateFulfilmentUnitLine> Lines,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateFulfilmentUnitLine(
    Guid OrderLineId,
    int Quantity);

public sealed record CreateFulfilmentUnitResult(
    bool Replayed,
    FulfilmentRequestStatus Status);

public sealed class FulfilmentRequestNotFoundException : Exception
{
    public FulfilmentRequestNotFoundException(
        Guid orderId)
        : base(
            $"Fulfilment request for order '{orderId}' was not found.")
    {
    }
}
