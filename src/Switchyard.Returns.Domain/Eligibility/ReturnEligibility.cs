namespace Switchyard.Returns.Domain.Eligibility;

public sealed class ReturnEligibility
{
    private readonly List<ReturnEligibilityLine> _lines;
    private readonly List<ShipmentDeliveryFact> _deliveries;

    private ReturnEligibility(
        Guid orderId,
        string orderNumber,
        IEnumerable<ReturnEligibilityLine> lines,
        IEnumerable<ShipmentDeliveryFact> deliveries)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            throw new ArgumentException("Order number is required.", nameof(orderNumber));
        }

        OrderId = orderId;
        OrderNumber = orderNumber.Trim();
        _lines = lines.ToList();
        _deliveries = deliveries.ToList();
    }

    public Guid OrderId { get; }
    public string OrderNumber { get; }
    public IReadOnlyList<ReturnEligibilityLine> Lines => _lines;
    public IReadOnlyList<ShipmentDeliveryFact> Deliveries => _deliveries;

    public static ReturnEligibility Create(Guid orderId, string orderNumber) =>
        new(orderId, orderNumber, [], []);

    public static ReturnEligibility Rehydrate(
        Guid orderId,
        string orderNumber,
        IEnumerable<ReturnEligibilityLine> lines,
        IEnumerable<ShipmentDeliveryFact> deliveries) =>
        new(orderId, orderNumber, lines, deliveries);

    public bool RecordDelivery(
        Guid shipmentId,
        IEnumerable<DeliveredLine> lines,
        DateTimeOffset deliveredAtUtc)
    {
        if (shipmentId == Guid.Empty)
        {
            throw new ArgumentException("Shipment ID cannot be empty.", nameof(shipmentId));
        }

        if (deliveredAtUtc == default)
        {
            throw new ArgumentException("Delivery time is required.", nameof(deliveredAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var normalized =
            lines.GroupBy(line => line.OrderLineId)
                 .Select(group => new DeliveredLine(
                     group.Key,
                     checked(group.Sum(line => line.Quantity))))
                 .OrderBy(line => line.OrderLineId)
                 .ToArray();

        if (normalized.Length == 0 ||
            normalized.Any(line => line.OrderLineId == Guid.Empty || line.Quantity <= 0))
        {
            throw new ArgumentException(
                "A delivered shipment requires positive quantities for valid order lines.",
                nameof(lines));
        }

        var existing =
            _deliveries.SingleOrDefault(delivery => delivery.ShipmentId == shipmentId);

        if (existing is not null)
        {
            if (existing.DeliveredAtUtc != deliveredAtUtc ||
                !existing.Lines.SequenceEqual(normalized))
            {
                throw new ReturnEligibilityConflictException(OrderId, shipmentId);
            }

            return false;
        }

        foreach (var deliveredLine in normalized)
        {
            var eligibilityLine =
                _lines.SingleOrDefault(line => line.OrderLineId == deliveredLine.OrderLineId);

            if (eligibilityLine is null)
            {
                _lines.Add(new ReturnEligibilityLine(
                    deliveredLine.OrderLineId,
                    deliveredLine.Quantity,
                    returnedQuantity: 0));
            }
            else
            {
                eligibilityLine.AddDelivered(deliveredLine.Quantity);
            }
        }

        _deliveries.Add(new ShipmentDeliveryFact(
            shipmentId,
            deliveredAtUtc,
            normalized));

        return true;
    }

    public int GetReturnableQuantity(Guid orderLineId) =>
        _lines.SingleOrDefault(candidate => candidate.OrderLineId == orderLineId)
              ?.ReturnableQuantity ?? 0;
}

public sealed class ReturnEligibilityLine
{
    public ReturnEligibilityLine(Guid orderLineId, int deliveredQuantity, int returnedQuantity)
    {
        if (orderLineId == Guid.Empty)
        {
            throw new ArgumentException("Order line ID cannot be empty.", nameof(orderLineId));
        }

        if (deliveredQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveredQuantity));
        }

        if (returnedQuantity < 0 || returnedQuantity > deliveredQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(returnedQuantity));
        }

        OrderLineId = orderLineId;
        DeliveredQuantity = deliveredQuantity;
        ReturnedQuantity = returnedQuantity;
    }

    public Guid OrderLineId { get; }
    public int DeliveredQuantity { get; private set; }
    public int ReturnedQuantity { get; private set; }
    public int ReturnableQuantity => DeliveredQuantity - ReturnedQuantity;

    internal void AddDelivered(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        DeliveredQuantity = checked(DeliveredQuantity + quantity);
    }
}

public sealed record DeliveredLine(Guid OrderLineId, int Quantity);

public sealed record ShipmentDeliveryFact(
    Guid ShipmentId,
    DateTimeOffset DeliveredAtUtc,
    IReadOnlyList<DeliveredLine> Lines);

public sealed class ReturnEligibilityConflictException : Exception
{
    public ReturnEligibilityConflictException(Guid orderId, Guid shipmentId)
        : base($"Shipment '{shipmentId}' for order '{orderId}' was already projected with different delivery facts.")
    {
    }
}
