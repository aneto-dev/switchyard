namespace Switchyard.Ordering.Application.Orders;

public sealed record CreatePendingOrderCommand(
    IReadOnlyCollection<CreatePendingOrderLine> Lines);
