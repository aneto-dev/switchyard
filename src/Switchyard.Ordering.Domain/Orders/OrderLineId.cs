namespace Switchyard.Ordering.Domain.Orders;

public sealed record OrderLineId
{
    public OrderLineId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Order line ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static OrderLineId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
