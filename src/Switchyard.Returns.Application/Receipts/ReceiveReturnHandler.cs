using Switchyard.Returns.Application.Eligibility;
using Switchyard.Returns.Application.Requests;
using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Application.Receipts;

public sealed class ReceiveReturnHandler
{
    private readonly IReturnEligibilityRepository _repository;
    private readonly IReturnsUnitOfWork _unitOfWork;

    public ReceiveReturnHandler(
        IReturnEligibilityRepository repository,
        IReturnsUnitOfWork unitOfWork)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(
                nameof(repository));

        _unitOfWork =
            unitOfWork ??
            throw new ArgumentNullException(
                nameof(unitOfWork));
    }

    public async Task<ReceiveReturnResult> HandleAsync(
        ReceiveReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        await using var transaction =
            await _unitOfWork.BeginTransactionAsync(
                cancellationToken);

        var eligibility =
            await _repository.GetByOrderIdForUpdateAsync(
                command.OrderId,
                cancellationToken) ??
            throw new ReturnOrderNotEligibleException(
                command.OrderId);

        var changed =
            eligibility.ReceiveReturn(
                command.ReturnRequestId,
                command.ReturnReceiptId,
                command.Lines.Select(
                    line =>
                        new ReturnReceiptLine(
                            line.OrderLineId,
                            line.Quantity)),
                command.ReceivedAtUtc);

        if (changed)
        {
            await _repository.SaveAsync(
                eligibility,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        var request =
            eligibility.ReturnRequests.Single(
                candidate =>
                    candidate.ReturnRequestId ==
                    command.ReturnRequestId);

        return new ReceiveReturnResult(
            command.ReturnReceiptId,
            request.Status,
            Replayed: !changed);
    }
}

public sealed record ReceiveReturnCommand(
    Guid ReturnReceiptId,
    Guid ReturnRequestId,
    Guid OrderId,
    IReadOnlyList<ReceiveReturnLine> Lines,
    DateTimeOffset ReceivedAtUtc);

public sealed record ReceiveReturnLine(
    Guid OrderLineId,
    int Quantity);

public sealed record ReceiveReturnResult(
    Guid ReturnReceiptId,
    ReturnRequestStatus Status,
    bool Replayed);
