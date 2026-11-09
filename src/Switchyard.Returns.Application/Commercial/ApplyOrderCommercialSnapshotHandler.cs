namespace Switchyard.Returns.Application.Commercial;

public sealed class ApplyOrderCommercialSnapshotHandler
{
    private readonly IReturnCommercialSnapshotRepository _repository;

    public ApplyOrderCommercialSnapshotHandler(
        IReturnCommercialSnapshotRepository repository)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(
                nameof(repository));
    }

    public Task<ApplyOrderCommercialSnapshotResult> HandleAsync(
        ApplyOrderCommercialSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        return _repository.ApplyAsync(
            new ReturnCommercialSnapshot(
                command.OrderId,
                command.PaymentId,
                command.OrderNumber,
                command.TotalAmount,
                command.Currency,
                command.ConfirmedAtUtc,
                command.Lines.Select(
                    line =>
                        new ReturnCommercialSnapshotLine(
                            line.OrderLineId,
                            line.Quantity,
                            line.UnitPriceAmount,
                            line.Currency))
                    .ToArray()),
            cancellationToken);
    }
}

public sealed record ApplyOrderCommercialSnapshotCommand(
    Guid OrderId,
    Guid PaymentId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<ApplyOrderCommercialSnapshotLine> Lines);

public sealed record ApplyOrderCommercialSnapshotLine(
    Guid OrderLineId,
    int Quantity,
    decimal UnitPriceAmount,
    string Currency);

public sealed record ApplyOrderCommercialSnapshotResult(
    bool Replayed);

public sealed record ReturnCommercialSnapshot(
    Guid OrderId,
    Guid PaymentId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<ReturnCommercialSnapshotLine> Lines);

public sealed record ReturnCommercialSnapshotLine(
    Guid OrderLineId,
    int Quantity,
    decimal UnitPriceAmount,
    string Currency);

public interface IReturnCommercialSnapshotRepository
{
    Task<ApplyOrderCommercialSnapshotResult> ApplyAsync(
        ReturnCommercialSnapshot snapshot,
        CancellationToken cancellationToken);

    Task<ReturnCommercialSnapshot?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken);
}
