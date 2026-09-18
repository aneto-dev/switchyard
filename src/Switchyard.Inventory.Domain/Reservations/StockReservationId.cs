namespace Switchyard.Inventory.Domain.Reservations;

public sealed record StockReservationId
{
    public StockReservationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Stock reservation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static StockReservationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
