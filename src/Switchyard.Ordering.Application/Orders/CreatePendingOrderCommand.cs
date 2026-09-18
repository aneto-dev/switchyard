namespace Switchyard.Ordering.Application.Orders;

public sealed record CreatePendingOrderCommand(
    string IdempotencyKey,
    IReadOnlyCollection<CreatePendingOrderLine> Lines);
