using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderPlacementLine
{
    private OrderPlacementLine(
        OrderLineId orderLineId,
        Guid reservationRequestId,
        string skuCode,
        int quantity,
        OrderPlacementLineState state,
        Guid? reservationId,
        DateTimeOffset? reservationExpiresAtUtc)
    {
        OrderLineId = orderLineId;
        ReservationRequestId = reservationRequestId;
        SkuCode = skuCode;
        Quantity = quantity;
        State = state;
        ReservationId = reservationId;
        ReservationExpiresAtUtc =
            reservationExpiresAtUtc?.ToUniversalTime();
    }

    public OrderLineId OrderLineId { get; }
    public Guid ReservationRequestId { get; }
    public string SkuCode { get; }
    public int Quantity { get; }
    public OrderPlacementLineState State { get; }
    public Guid? ReservationId { get; }
    public DateTimeOffset? ReservationExpiresAtUtc { get; }

    internal static OrderPlacementLine Start(OrderLine orderLine)
    {
        ArgumentNullException.ThrowIfNull(orderLine);

        return new OrderPlacementLine(
            orderLine.Id,
            Guid.NewGuid(),
            orderLine.Product.SkuCode.Value,
            orderLine.Quantity,
            OrderPlacementLineState.AwaitingReservation,
            reservationId: null,
            reservationExpiresAtUtc: null);
    }

    public static OrderPlacementLine Rehydrate(
        OrderLineId orderLineId,
        Guid reservationRequestId,
        string skuCode,
        int quantity,
        OrderPlacementLineState state,
        Guid? reservationId,
        DateTimeOffset? reservationExpiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(orderLineId);

        if (reservationRequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation request ID cannot be empty.",
                nameof(reservationRequestId));
        }

        if (string.IsNullOrWhiteSpace(skuCode))
        {
            throw new ArgumentException("SKU code is required.", nameof(skuCode));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be positive.");
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown order-placement line state.");
        }

        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation ID cannot be empty.",
                nameof(reservationId));
        }

        if (reservationExpiresAtUtc.HasValue &&
            reservationExpiresAtUtc.Value == default)
        {
            throw new ArgumentException(
                "Reservation expiry cannot be the default timestamp.",
                nameof(reservationExpiresAtUtc));
        }

        var requiresReservation =
            state is OrderPlacementLineState.Reserved
                or OrderPlacementLineState.AwaitingRelease
                or OrderPlacementLineState.Released
                or OrderPlacementLineState.Expired;

        if (requiresReservation != reservationId.HasValue ||
            (!requiresReservation && reservationExpiresAtUtc.HasValue))
        {
            throw new ArgumentException(
                "Reservation identity and expiry do not match the order-placement line state.");
        }

        return new OrderPlacementLine(
            orderLineId,
            reservationRequestId,
            skuCode.Trim(),
            quantity,
            state,
            reservationId,
            reservationExpiresAtUtc);
    }
}
