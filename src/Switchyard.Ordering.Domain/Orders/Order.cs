namespace Switchyard.Ordering.Domain.Orders;

public sealed class Order
{
    private readonly IReadOnlyList<OrderLine> _lines;

    private Order(
        OrderId id,
        OrderNumber orderNumber,
        IReadOnlyList<OrderLine> lines,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrderNumber = orderNumber;
        _lines = lines;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Status = OrderStatus.Pending;
        Total = CalculateTotal(lines);
    }

    public OrderId Id { get; }

    public OrderNumber OrderNumber { get; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; }

    public OrderStatus Status { get; private set; }

    public Money Total { get; }

    public static Order Create(
        OrderId id,
        OrderNumber orderNumber,
        IEnumerable<OrderLine> lines,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(orderNumber);
        ArgumentNullException.ThrowIfNull(lines);

        var materializedLines = lines.ToArray();

        if (materializedLines.Length == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        if (materializedLines.Any(line => line is null))
        {
            throw new ArgumentException("Order lines cannot contain null entries.", nameof(lines));
        }

        var duplicateLineId = materializedLines
            .GroupBy(line => line.Id)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateLineId is not null)
        {
            throw new ArgumentException("Order line IDs must be unique within an order.", nameof(lines));
        }

        var currency = materializedLines[0].UnitPrice.Currency;

        if (materializedLines.Any(line => !string.Equals(line.UnitPrice.Currency, currency, StringComparison.Ordinal)))
        {
            throw new ArgumentException("All order lines must use the same currency.", nameof(lines));
        }

        return new Order(id, orderNumber, Array.AsReadOnly(materializedLines), createdAtUtc);
    }

    public void Confirm()
    {
        EnsurePending("confirm");
        Status = OrderStatus.Confirmed;
    }

    public void Fail()
    {
        EnsurePending("fail");
        Status = OrderStatus.Failed;
    }

    private static Money CalculateTotal(IReadOnlyList<OrderLine> lines)
    {
        var total = new Money(0m, lines[0].UnitPrice.Currency);

        foreach (var line in lines)
        {
            total += line.LineTotal;
        }

        return total;
    }

    private void EnsurePending(string transition)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot {transition} an order in status {Status}.");
        }
    }
}
