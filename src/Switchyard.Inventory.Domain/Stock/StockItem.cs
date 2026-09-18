namespace Switchyard.Inventory.Domain.Stock;

public sealed class StockItem
{
    public StockItem(InventorySku sku, int onHandQuantity, int reservedQuantity)
    {
        ArgumentNullException.ThrowIfNull(sku);

        if (onHandQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(onHandQuantity),
                "On-hand quantity cannot be negative.");
        }

        if (reservedQuantity < 0 || reservedQuantity > onHandQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedQuantity),
                "Reserved quantity must be between zero and the on-hand quantity.");
        }

        Sku = sku;
        OnHandQuantity = onHandQuantity;
        ReservedQuantity = reservedQuantity;
    }

    public InventorySku Sku { get; }

    public int OnHandQuantity { get; }

    public int ReservedQuantity { get; }

    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public StockItem Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Reservation quantity must be positive.");
        }

        if (quantity > AvailableQuantity)
        {
            throw new InvalidOperationException("Insufficient stock is available for the reservation.");
        }

        return new StockItem(Sku, OnHandQuantity, ReservedQuantity + quantity);
    }
}
