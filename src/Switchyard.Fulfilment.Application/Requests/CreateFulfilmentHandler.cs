using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Application.Requests;

public sealed class CreateFulfilmentHandler
{
    private readonly IFulfilmentRequestRepository _repository;

    public CreateFulfilmentHandler(
        IFulfilmentRequestRepository repository)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateFulfilmentResult> HandleAsync(
        CreateFulfilmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        var request =
            FulfilmentRequest.Create(
                command.OrderId,
                command.OrderNumber,
                command.Lines.Select(
                    line =>
                        new FulfilmentRequestLine(
                            line.OrderLineId,
                            line.SkuCode,
                            line.ProductName,
                            line.Quantity)),
                command.ConfirmedAtUtc);

        var existing =
            await _repository.GetByOrderIdAsync(
                command.OrderId,
                cancellationToken);

        if (existing is not null)
        {
            if (!Matches(existing, request))
            {
                throw new FulfilmentRequestConflictException(
                    command.OrderId);
            }

            return new CreateFulfilmentResult(
                Replayed: true);
        }

        await _repository.AddAsync(
            request,
            cancellationToken);

        return new CreateFulfilmentResult(
            Replayed: false);
    }

    private static bool Matches(
        FulfilmentRequest existing,
        FulfilmentRequest requested)
    {
        if (!string.Equals(
                existing.OrderNumber,
                requested.OrderNumber,
                StringComparison.Ordinal) ||
            existing.ConfirmedAtUtc != requested.ConfirmedAtUtc ||
            existing.Lines.Count != requested.Lines.Count)
        {
            return false;
        }

        return existing.Lines
            .Zip(
                requested.Lines,
                (left, right) =>
                    left.OrderLineId == right.OrderLineId &&
                    string.Equals(
                        left.SkuCode,
                        right.SkuCode,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        left.ProductName,
                        right.ProductName,
                        StringComparison.Ordinal) &&
                    left.Quantity == right.Quantity)
            .All(matches => matches);
    }
}

public sealed record CreateFulfilmentCommand(
    Guid OrderId,
    string OrderNumber,
    IReadOnlyList<CreateFulfilmentLine> Lines,
    DateTimeOffset ConfirmedAtUtc);

public sealed record CreateFulfilmentLine(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity);

public sealed record CreateFulfilmentResult(
    bool Replayed);

public interface IFulfilmentRequestRepository
{
    Task<FulfilmentRequest?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    Task AddAsync(
        FulfilmentRequest request,
        CancellationToken cancellationToken);
}

public sealed class FulfilmentRequestConflictException : Exception
{
    public FulfilmentRequestConflictException(
        Guid orderId)
        : base(
            $"Fulfilment request for order '{orderId}' already exists with different confirmed-order details.")
    {
    }
}
