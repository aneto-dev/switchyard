namespace Switchyard.Returns.Domain.Eligibility;

public sealed class ReturnEligibility
{
    private readonly List<ReturnEligibilityLine> _lines;
    private readonly List<ShipmentDeliveryFact> _deliveries;
    private readonly List<ReturnRequest> _returnRequests;

    private ReturnEligibility(
        Guid orderId,
        string orderNumber,
        IEnumerable<ReturnEligibilityLine> lines,
        IEnumerable<ShipmentDeliveryFact> deliveries,
        IEnumerable<ReturnRequest> returnRequests)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order ID cannot be empty.",
                nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            throw new ArgumentException(
                "Order number is required.",
                nameof(orderNumber));
        }

        OrderId = orderId;
        OrderNumber = orderNumber.Trim();
        _lines = lines.ToList();
        _deliveries = deliveries.ToList();
        _returnRequests = returnRequests.ToList();
    }

    public Guid OrderId { get; }
    public string OrderNumber { get; }
    public IReadOnlyList<ReturnEligibilityLine> Lines => _lines;
    public IReadOnlyList<ShipmentDeliveryFact> Deliveries => _deliveries;
    public IReadOnlyList<ReturnRequest> ReturnRequests => _returnRequests;

    public static ReturnEligibility Create(
        Guid orderId,
        string orderNumber) =>
        new(
            orderId,
            orderNumber,
            [],
            [],
            []);

    public static ReturnEligibility Rehydrate(
        Guid orderId,
        string orderNumber,
        IEnumerable<ReturnEligibilityLine> lines,
        IEnumerable<ShipmentDeliveryFact> deliveries,
        IEnumerable<ReturnRequest>? returnRequests = null) =>
        new(
            orderId,
            orderNumber,
            lines,
            deliveries,
            returnRequests ?? []);

    public bool RecordDelivery(
        Guid shipmentId,
        IEnumerable<DeliveredLine> lines,
        DateTimeOffset deliveredAtUtc)
    {
        if (shipmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Shipment ID cannot be empty.",
                nameof(shipmentId));
        }

        if (deliveredAtUtc == default)
        {
            throw new ArgumentException(
                "Delivery time is required.",
                nameof(deliveredAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var normalized =
            NormalizeLines(
                lines.Select(
                    line =>
                        new QuantityLine(
                            line.OrderLineId,
                            line.Quantity)),
                nameof(lines))
            .Select(
                line =>
                    new DeliveredLine(
                        line.OrderLineId,
                        line.Quantity))
            .ToArray();

        var existing =
            _deliveries.SingleOrDefault(
                delivery =>
                    delivery.ShipmentId ==
                    shipmentId);

        if (existing is not null)
        {
            if (existing.DeliveredAtUtc !=
                    deliveredAtUtc ||
                !existing.Lines.SequenceEqual(
                    normalized))
            {
                throw new ReturnEligibilityConflictException(
                    OrderId,
                    shipmentId);
            }

            return false;
        }

        foreach (var deliveredLine in normalized)
        {
            var eligibilityLine =
                _lines.SingleOrDefault(
                    line =>
                        line.OrderLineId ==
                        deliveredLine.OrderLineId);

            if (eligibilityLine is null)
            {
                _lines.Add(
                    new ReturnEligibilityLine(
                        deliveredLine.OrderLineId,
                        deliveredLine.Quantity,
                        authorisedReturnQuantity: 0,
                        returnedQuantity: 0));
            }
            else
            {
                eligibilityLine.AddDelivered(
                    deliveredLine.Quantity);
            }
        }

        _deliveries.Add(
            new ShipmentDeliveryFact(
                shipmentId,
                deliveredAtUtc,
                normalized));

        return true;
    }

    public bool AuthoriseReturn(
        Guid returnRequestId,
        IEnumerable<ReturnRequestLine> lines,
        DateTimeOffset requestedAtUtc)
    {
        if (returnRequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Return request ID cannot be empty.",
                nameof(returnRequestId));
        }

        if (requestedAtUtc == default)
        {
            throw new ArgumentException(
                "Return request time is required.",
                nameof(requestedAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var normalized =
            NormalizeLines(
                lines.Select(
                    line =>
                        new QuantityLine(
                            line.OrderLineId,
                            line.Quantity)),
                nameof(lines))
            .Select(
                line =>
                    new ReturnRequestLine(
                        line.OrderLineId,
                        line.Quantity))
            .ToArray();

        var existing =
            _returnRequests.SingleOrDefault(
                request =>
                    request.ReturnRequestId ==
                    returnRequestId);

        if (existing is not null)
        {
            if (existing.RequestedAtUtc !=
                    requestedAtUtc ||
                !existing.Lines.SequenceEqual(
                    normalized))
            {
                throw new ReturnRequestConflictException(
                    OrderId,
                    returnRequestId);
            }

            return false;
        }

        foreach (var requestedLine in normalized)
        {
            var eligibilityLine =
                _lines.SingleOrDefault(
                    line =>
                        line.OrderLineId ==
                        requestedLine.OrderLineId) ??
                throw new ReturnQuantityNotEligibleException(
                    requestedLine.OrderLineId,
                    requestedLine.Quantity,
                    returnableQuantity: 0);

            if (requestedLine.Quantity >
                eligibilityLine.ReturnableQuantity)
            {
                throw new ReturnQuantityNotEligibleException(
                    requestedLine.OrderLineId,
                    requestedLine.Quantity,
                    eligibilityLine.ReturnableQuantity);
            }
        }

        foreach (var requestedLine in normalized)
        {
            _lines.Single(
                    line =>
                        line.OrderLineId ==
                        requestedLine.OrderLineId)
                .AuthoriseReturn(
                    requestedLine.Quantity);
        }

        _returnRequests.Add(
            new ReturnRequest(
                returnRequestId,
                requestedAtUtc,
                ReturnRequestStatus.Authorised,
                normalized));

        return true;
    }

    public int GetReturnableQuantity(
        Guid orderLineId) =>
        _lines.SingleOrDefault(
                candidate =>
                    candidate.OrderLineId ==
                    orderLineId)
              ?.ReturnableQuantity ?? 0;

    private static QuantityLine[] NormalizeLines(
        IEnumerable<QuantityLine> lines,
        string parameterName)
    {
        var normalized =
            lines.GroupBy(
                    line =>
                        line.OrderLineId)
                 .Select(
                     group =>
                         new QuantityLine(
                             group.Key,
                             checked(
                                 group.Sum(
                                     line =>
                                         line.Quantity))))
                 .OrderBy(
                     line =>
                         line.OrderLineId)
                 .ToArray();

        if (normalized.Length == 0 ||
            normalized.Any(
                line =>
                    line.OrderLineId == Guid.Empty ||
                    line.Quantity <= 0))
        {
            throw new ArgumentException(
                "Positive quantities for valid order lines are required.",
                parameterName);
        }

        return normalized;
    }

    private sealed record QuantityLine(
        Guid OrderLineId,
        int Quantity);
}

public sealed class ReturnEligibilityLine
{
    public ReturnEligibilityLine(
        Guid orderLineId,
        int deliveredQuantity,
        int authorisedReturnQuantity,
        int returnedQuantity)
    {
        if (orderLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order line ID cannot be empty.",
                nameof(orderLineId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(
            deliveredQuantity);

        ArgumentOutOfRangeException.ThrowIfNegative(
            authorisedReturnQuantity);

        ArgumentOutOfRangeException.ThrowIfNegative(
            returnedQuantity);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            returnedQuantity,
            deliveredQuantity);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            authorisedReturnQuantity,
            deliveredQuantity -
            returnedQuantity);

        OrderLineId = orderLineId;
        DeliveredQuantity = deliveredQuantity;
        AuthorisedReturnQuantity =
            authorisedReturnQuantity;
        ReturnedQuantity = returnedQuantity;
    }

    public Guid OrderLineId { get; }
    public int DeliveredQuantity { get; private set; }
    public int AuthorisedReturnQuantity { get; private set; }
    public int ReturnedQuantity { get; private set; }

    public int ReturnableQuantity =>
        DeliveredQuantity -
        AuthorisedReturnQuantity -
        ReturnedQuantity;

    internal void AddDelivered(
        int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            quantity);

        DeliveredQuantity =
            checked(
                DeliveredQuantity +
                quantity);
    }

    internal void AuthoriseReturn(
        int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            quantity);

        if (quantity >
            ReturnableQuantity)
        {
            throw new InvalidOperationException(
                "Authorised return quantity cannot exceed returnable quantity.");
        }

        AuthorisedReturnQuantity =
            checked(
                AuthorisedReturnQuantity +
                quantity);
    }
}

public enum ReturnRequestStatus
{
    Authorised = 0
}

public sealed record ReturnRequest(
    Guid ReturnRequestId,
    DateTimeOffset RequestedAtUtc,
    ReturnRequestStatus Status,
    IReadOnlyList<ReturnRequestLine> Lines);

public sealed record ReturnRequestLine(
    Guid OrderLineId,
    int Quantity);

public sealed record DeliveredLine(
    Guid OrderLineId,
    int Quantity);

public sealed record ShipmentDeliveryFact(
    Guid ShipmentId,
    DateTimeOffset DeliveredAtUtc,
    IReadOnlyList<DeliveredLine> Lines);

public sealed class ReturnEligibilityConflictException : Exception
{
    public ReturnEligibilityConflictException(
        Guid orderId,
        Guid shipmentId)
        : base(
            $"Shipment '{shipmentId}' for order '{orderId}' was already projected with different delivery facts.")
    {
    }
}

public sealed class ReturnRequestConflictException : Exception
{
    public ReturnRequestConflictException(
        Guid orderId,
        Guid returnRequestId)
        : base(
            $"Return request '{returnRequestId}' for order '{orderId}' already exists with different details.")
    {
    }
}

public sealed class ReturnQuantityNotEligibleException : Exception
{
    public ReturnQuantityNotEligibleException(
        Guid orderLineId,
        int requestedQuantity,
        int returnableQuantity)
        : base(
            $"Order line '{orderLineId}' requested return quantity {requestedQuantity}, but only {returnableQuantity} is returnable.")
    {
        OrderLineId = orderLineId;
        RequestedQuantity = requestedQuantity;
        ReturnableQuantity = returnableQuantity;
    }

    public Guid OrderLineId { get; }
    public int RequestedQuantity { get; }
    public int ReturnableQuantity { get; }
}
