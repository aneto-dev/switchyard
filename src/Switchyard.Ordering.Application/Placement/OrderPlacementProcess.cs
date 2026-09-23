using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderPlacementProcess
{
    private readonly IReadOnlyList<OrderPlacementLine> _lines;

    private OrderPlacementProcess(
        OrderId orderId,
        OrderPlacementState state,
        DateTimeOffset startedAtUtc,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<OrderPlacementLine> lines)
    {
        OrderId = orderId;
        State = state;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        _lines = lines;
    }

    public OrderId OrderId { get; }
    public OrderPlacementState State { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public IReadOnlyList<OrderPlacementLine> Lines => _lines;

    public static OrderPlacementProcess Start(
        Order order,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException(
                "Order placement can start only for a pending order.");
        }

        var lines = order.Lines
            .Select(OrderPlacementLine.Start)
            .ToArray();

        return new OrderPlacementProcess(
            order.Id,
            OrderPlacementState.AwaitingInventory,
            startedAtUtc,
            startedAtUtc,
            Array.AsReadOnly(lines));
    }

    public static OrderPlacementProcess Rehydrate(
        OrderId orderId,
        OrderPlacementState state,
        DateTimeOffset startedAtUtc,
        DateTimeOffset updatedAtUtc,
        IEnumerable<OrderPlacementLine> lines)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(lines);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown order-placement state.");
        }

        if (updatedAtUtc < startedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAtUtc),
                "Updated time cannot be before start time.");
        }

        var materializedLines = lines.ToArray();

        if (materializedLines.Length == 0)
        {
            throw new ArgumentException(
                "Order placement requires at least one line.",
                nameof(lines));
        }

        if (materializedLines
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Order-placement line IDs must be unique.",
                nameof(lines));
        }

        if (materializedLines
            .GroupBy(line => line.ReservationRequestId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Reservation request IDs must be unique.",
                nameof(lines));
        }

        return new OrderPlacementProcess(
            orderId,
            state,
            startedAtUtc,
            updatedAtUtc,
            Array.AsReadOnly(materializedLines));
    }
}
