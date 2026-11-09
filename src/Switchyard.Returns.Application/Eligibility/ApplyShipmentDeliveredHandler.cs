using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Application.Eligibility;

public sealed class ApplyShipmentDeliveredHandler
{
    private readonly IReturnEligibilityRepository _repository;

    public ApplyShipmentDeliveredHandler(IReturnEligibilityRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ApplyShipmentDeliveredResult> HandleAsync(
        ApplyShipmentDeliveredCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        var eligibility =
            await _repository.GetByOrderIdForUpdateAsync(
                command.OrderId,
                cancellationToken) ??
            ReturnEligibility.Create(command.OrderId, command.OrderNumber);

        if (!string.Equals(
                eligibility.OrderNumber,
                command.OrderNumber,
                StringComparison.Ordinal))
        {
            throw new ReturnEligibilityConflictException(
                command.OrderId,
                command.ShipmentId);
        }

        var changed =
            eligibility.RecordDelivery(
                command.ShipmentId,
                command.Lines.Select(line => new DeliveredLine(
                    line.OrderLineId,
                    line.Quantity)),
                command.DeliveredAtUtc);

        if (changed)
        {
            await _repository.SaveAsync(
                eligibility,
                cancellationToken);
        }

        return new ApplyShipmentDeliveredResult(Replayed: !changed);
    }
}

public sealed record ApplyShipmentDeliveredCommand(
    Guid OrderId,
    Guid ShipmentId,
    string OrderNumber,
    IReadOnlyList<ApplyShipmentDeliveredLine> Lines,
    DateTimeOffset DeliveredAtUtc);

public sealed record ApplyShipmentDeliveredLine(Guid OrderLineId, int Quantity);

public sealed record ApplyShipmentDeliveredResult(bool Replayed);

public interface IReturnEligibilityRepository
{
    Task<ReturnEligibility?> GetByOrderIdForUpdateAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ReturnEligibility eligibility,
        CancellationToken cancellationToken);
}
