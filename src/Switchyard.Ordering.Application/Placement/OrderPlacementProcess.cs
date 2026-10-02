using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderPlacementProcess
{
    private readonly OrderPlacementLine[] _lines;
    private readonly IReadOnlyList<OrderPlacementLine> _readOnlyLines;

    private OrderPlacementProcess(
        OrderId orderId,
        OrderPlacementState state,
        Guid? paymentAuthorisationRequestId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<OrderPlacementLine> lines)
    {
        OrderId = orderId;
        State = state;
        PaymentAuthorisationRequestId = paymentAuthorisationRequestId;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        _lines = lines.ToArray();
        _readOnlyLines = Array.AsReadOnly(_lines);
    }

    public OrderId OrderId { get; }
    public OrderPlacementState State { get; private set; }
    public Guid? PaymentAuthorisationRequestId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<OrderPlacementLine> Lines => _readOnlyLines;

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
            paymentAuthorisationRequestId: null,
            startedAtUtc,
            startedAtUtc,
            lines);
    }

    public OrderPlacementInventoryTransition RecordInventoryReserved(
        Guid requestId,
        OrderLineId orderLineId,
        string skuCode,
        int quantity,
        Guid reservationId,
        Guid paymentAuthorisationRequestId,
        DateTimeOffset updatedAtUtc)
    {
        if (reservationId == Guid.Empty)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Inventory reserved outcome requires a reservation ID.");
        }

        if (paymentAuthorisationRequestId == Guid.Empty)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Payment authorisation request ID cannot be empty.");
        }

        var index = FindLineIndex(
            requestId,
            orderLineId,
            skuCode,
            quantity);
        var line = _lines[index];

        if (line.State is (
                OrderPlacementLineState.Released or
                OrderPlacementLineState.Expired) &&
            line.ReservationId == reservationId)
        {
            return OrderPlacementInventoryTransition.None;
        }

        if (line.State == OrderPlacementLineState.AwaitingRelease &&
            line.ReservationId == reservationId)
        {
            return OrderPlacementInventoryTransition.None;
        }

        if (line.State == OrderPlacementLineState.Reserved &&
            line.ReservationId == reservationId)
        {
            if (State == OrderPlacementState.CompensatingInventory)
            {
                _lines[index] = RehydrateLine(
                    line,
                    OrderPlacementLineState.AwaitingRelease,
                    reservationId);

                Touch(updatedAtUtc);

                return new OrderPlacementInventoryTransition(
                    Changed: true,
                    AuthorisePayment: false,
                    FailPlacement: false,
                    new[]
                    {
                        new OrderPlacementRelease(
                            line.ReservationRequestId,
                            reservationId)
                    });
            }

            return OrderPlacementInventoryTransition.None;
        }

        if (line.State != OrderPlacementLineState.AwaitingReservation)
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Inventory reserved outcome conflicts with line state '{line.State}'.");
        }

        if (State == OrderPlacementState.AwaitingInventory)
        {
            _lines[index] = RehydrateLine(
                line,
                OrderPlacementLineState.Reserved,
                reservationId);

            var authorisePayment =
                _lines.All(
                    candidate =>
                        candidate.State ==
                        OrderPlacementLineState.Reserved);

            if (authorisePayment)
            {
                State = OrderPlacementState.AwaitingPayment;
                PaymentAuthorisationRequestId =
                    paymentAuthorisationRequestId;
            }

            Touch(updatedAtUtc);

            return new OrderPlacementInventoryTransition(
                Changed: true,
                AuthorisePayment: authorisePayment,
                FailPlacement: false,
                Array.Empty<OrderPlacementRelease>());
        }

        if (State == OrderPlacementState.CompensatingInventory)
        {
            _lines[index] = RehydrateLine(
                line,
                OrderPlacementLineState.AwaitingRelease,
                reservationId);

            Touch(updatedAtUtc);

            return new OrderPlacementInventoryTransition(
                Changed: true,
                AuthorisePayment: false,
                FailPlacement: false,
                new[]
                {
                    new OrderPlacementRelease(
                        line.ReservationRequestId,
                        reservationId)
                });
        }

        throw new OrderPlacementInventoryOutcomeException(
            $"Inventory reserved outcome cannot advance process state '{State}'.");
    }

    public OrderPlacementInventoryTransition RecordInventoryRejected(
        Guid requestId,
        OrderLineId orderLineId,
        string skuCode,
        int quantity,
        DateTimeOffset updatedAtUtc)
    {
        var index = FindLineIndex(
            requestId,
            orderLineId,
            skuCode,
            quantity);
        var line = _lines[index];

        if (line.State == OrderPlacementLineState.Rejected)
        {
            return OrderPlacementInventoryTransition.None;
        }

        if (line.State != OrderPlacementLineState.AwaitingReservation)
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Inventory rejected outcome conflicts with line state '{line.State}'.");
        }

        if (State is not (
            OrderPlacementState.AwaitingInventory or
            OrderPlacementState.CompensatingInventory))
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Inventory rejected outcome cannot advance process state '{State}'.");
        }

        _lines[index] = RehydrateLine(
            line,
            OrderPlacementLineState.Rejected,
            reservationId: null);

        State = OrderPlacementState.CompensatingInventory;

        var releases =
            new List<OrderPlacementRelease>();

        for (var lineIndex = 0; lineIndex < _lines.Length; lineIndex++)
        {
            var candidate = _lines[lineIndex];

            if (candidate.State != OrderPlacementLineState.Reserved)
            {
                continue;
            }

            var reservationId =
                candidate.ReservationId ??
                throw new OrderPlacementInventoryOutcomeException(
                    "A reserved placement line is missing its reservation ID.");

            _lines[lineIndex] = RehydrateLine(
                candidate,
                OrderPlacementLineState.AwaitingRelease,
                reservationId);

            releases.Add(
                new OrderPlacementRelease(
                    candidate.ReservationRequestId,
                    reservationId));
        }

        var failPlacement =
            _lines.All(
                candidate =>
                    candidate.State is
                        OrderPlacementLineState.Rejected or
                        OrderPlacementLineState.Released or
                        OrderPlacementLineState.Expired);

        if (failPlacement)
        {
            State = OrderPlacementState.Failed;
        }

        Touch(updatedAtUtc);

        return new OrderPlacementInventoryTransition(
            Changed: true,
            AuthorisePayment: false,
            FailPlacement: failPlacement,
            releases.AsReadOnly());
    }

    public OrderPlacementInventoryTransition RecordInventoryReleased(
        Guid requestId,
        Guid reservationId,
        DateTimeOffset updatedAtUtc)
    {
        return RecordInventoryCompensationCompleted(
            requestId,
            reservationId,
            OrderPlacementLineState.Released,
            updatedAtUtc);
    }

    public OrderPlacementInventoryTransition RecordInventoryExpired(
        Guid requestId,
        Guid reservationId,
        DateTimeOffset updatedAtUtc)
    {
        return RecordInventoryCompensationCompleted(
            requestId,
            reservationId,
            OrderPlacementLineState.Expired,
            updatedAtUtc);
    }

    public static OrderPlacementProcess Rehydrate(
        OrderId orderId,
        OrderPlacementState state,
        Guid? paymentAuthorisationRequestId,
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

        if (paymentAuthorisationRequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment authorisation request ID cannot be empty.",
                nameof(paymentAuthorisationRequestId));
        }

        if (state is
            OrderPlacementState.AwaitingPayment or
            OrderPlacementState.PaymentIndeterminate or
            OrderPlacementState.Confirming or
            OrderPlacementState.Confirmed &&
            paymentAuthorisationRequestId is null)
        {
            throw new ArgumentException(
                $"Order-placement state '{state}' requires a payment authorisation request ID.",
                nameof(paymentAuthorisationRequestId));
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
            paymentAuthorisationRequestId,
            startedAtUtc,
            updatedAtUtc,
            materializedLines);
    }

    private OrderPlacementInventoryTransition RecordInventoryCompensationCompleted(
        Guid requestId,
        Guid reservationId,
        OrderPlacementLineState completedState,
        DateTimeOffset updatedAtUtc)
    {
        if (reservationId == Guid.Empty)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Inventory compensation outcome requires a reservation ID.");
        }

        if (completedState is not (
            OrderPlacementLineState.Released or
            OrderPlacementLineState.Expired))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedState),
                completedState,
                "Inventory compensation completion state is invalid.");
        }

        var index =
            FindLineIndex(
                requestId);

        var line =
            _lines[index];

        if (line.State == completedState &&
            line.ReservationId == reservationId)
        {
            return OrderPlacementInventoryTransition.None;
        }

        if (State != OrderPlacementState.CompensatingInventory)
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Inventory compensation outcome cannot advance process state '{State}'.");
        }

        if (line.State != OrderPlacementLineState.AwaitingRelease ||
            line.ReservationId != reservationId)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Inventory compensation outcome does not match the awaited reservation release.");
        }

        _lines[index] =
            RehydrateLine(
                line,
                completedState,
                reservationId);

        var failPlacement =
            _lines.All(
                candidate =>
                    candidate.State is
                        OrderPlacementLineState.Rejected or
                        OrderPlacementLineState.Released or
                        OrderPlacementLineState.Expired);

        if (failPlacement)
        {
            State =
                OrderPlacementState.Failed;
        }

        Touch(
            updatedAtUtc);

        return new OrderPlacementInventoryTransition(
            Changed: true,
            AuthorisePayment: false,
            FailPlacement: failPlacement,
            Array.Empty<OrderPlacementRelease>());
    }

    private int FindLineIndex(
        Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Inventory outcome request ID cannot be empty.");
        }

        var index =
            Array.FindIndex(
                _lines,
                line =>
                    line.ReservationRequestId ==
                    requestId);

        if (index < 0)
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Reservation request '{requestId}' does not belong to placement '{OrderId.Value}'.");
        }

        return index;
    }

    private int FindLineIndex(
        Guid requestId,
        OrderLineId orderLineId,
        string skuCode,
        int quantity)
    {
        if (requestId == Guid.Empty)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Inventory outcome request ID cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(orderLineId);

        if (string.IsNullOrWhiteSpace(skuCode) ||
            quantity <= 0)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Inventory outcome line identity is invalid.");
        }

        var index =
            Array.FindIndex(
                _lines,
                line =>
                    line.OrderLineId == orderLineId);

        if (index < 0)
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Order line '{orderLineId.Value}' does not belong to placement '{OrderId.Value}'.");
        }

        var existing = _lines[index];

        if (existing.ReservationRequestId != requestId ||
            !string.Equals(
                existing.SkuCode,
                skuCode.Trim(),
                StringComparison.Ordinal) ||
            existing.Quantity != quantity)
        {
            throw new OrderPlacementInventoryOutcomeException(
                $"Inventory outcome does not match placement line '{orderLineId.Value}'.");
        }

        return index;
    }

    private static OrderPlacementLine RehydrateLine(
        OrderPlacementLine line,
        OrderPlacementLineState state,
        Guid? reservationId)
    {
        return OrderPlacementLine.Rehydrate(
            line.OrderLineId,
            line.ReservationRequestId,
            line.SkuCode,
            line.Quantity,
            state,
            reservationId);
    }

    private void Touch(DateTimeOffset updatedAtUtc)
    {
        var normalized =
            updatedAtUtc.ToUniversalTime();

        if (normalized < UpdatedAtUtc)
        {
            throw new OrderPlacementInventoryOutcomeException(
                "Order-placement update time cannot move backwards.");
        }

        UpdatedAtUtc = normalized;
    }
}
