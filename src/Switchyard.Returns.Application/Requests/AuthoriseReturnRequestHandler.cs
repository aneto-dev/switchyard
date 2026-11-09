using Switchyard.Returns.Application.Eligibility;
using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Application.Requests;

public sealed class AuthoriseReturnRequestHandler
{
    private readonly IReturnEligibilityRepository _repository;
    private readonly IReturnsUnitOfWork _unitOfWork;

    public AuthoriseReturnRequestHandler(
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

    public async Task<AuthoriseReturnRequestResult> HandleAsync(
        AuthoriseReturnRequestCommand command,
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
            eligibility.AuthoriseReturn(
                command.ReturnRequestId,
                command.Lines.Select(
                    line =>
                        new ReturnRequestLine(
                            line.OrderLineId,
                            line.Quantity)),
                command.RequestedAtUtc);

        if (changed)
        {
            await _repository.SaveAsync(
                eligibility,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new AuthoriseReturnRequestResult(
            command.ReturnRequestId,
            Replayed: !changed);
    }
}

public sealed record AuthoriseReturnRequestCommand(
    Guid ReturnRequestId,
    Guid OrderId,
    IReadOnlyList<AuthoriseReturnRequestLine> Lines,
    DateTimeOffset RequestedAtUtc);

public sealed record AuthoriseReturnRequestLine(
    Guid OrderLineId,
    int Quantity);

public sealed record AuthoriseReturnRequestResult(
    Guid ReturnRequestId,
    bool Replayed);

public interface IReturnsUnitOfWork
{
    Task<IReturnsTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken);
}

public interface IReturnsTransaction : IAsyncDisposable
{
    Task CommitAsync(
        CancellationToken cancellationToken);
}

public sealed class ReturnOrderNotEligibleException : Exception
{
    public ReturnOrderNotEligibleException(
        Guid orderId)
        : base(
            $"Order '{orderId}' has no delivered quantity eligible for return.")
    {
    }
}
