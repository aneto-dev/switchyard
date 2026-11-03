using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Application.Units;
using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Application.Cancellations;

public sealed class CancelFulfilmentHandler
{
    private readonly IFulfilmentRequestRepository _repository;

    public CancelFulfilmentHandler(
        IFulfilmentRequestRepository repository)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CancelFulfilmentResult> HandleAsync(
        CancelFulfilmentCommand command,
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

        var decision =
            request.ApplyCancellation(
                command.CancellationId,
                command.Lines.Select(
                    line =>
                        new FulfilmentCancellationLine(
                            line.OrderLineId,
                            line.Quantity)),
                command.RequestedAtUtc);

        if (!decision.Replayed)
        {
            await _repository.SaveAsync(
                request,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new CancelFulfilmentResult(
            decision.Replayed,
            decision.Outcome,
            decision.RejectionReason);
    }
}

public sealed record CancelFulfilmentCommand(
    Guid OrderId,
    Guid CancellationId,
    IReadOnlyList<CancelFulfilmentLine> Lines,
    DateTimeOffset RequestedAtUtc);

public sealed record CancelFulfilmentLine(
    Guid OrderLineId,
    int Quantity);

public sealed record CancelFulfilmentResult(
    bool Replayed,
    FulfilmentCancellationOutcome Outcome,
    FulfilmentCancellationRejectionReason? RejectionReason);
