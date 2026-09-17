namespace Switchyard.Ordering.Domain.Orders;

public sealed record OrderId
{
    public OrderId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
