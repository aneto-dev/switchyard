namespace Switchyard.Ordering.Domain.Orders;

public sealed class OrderLine
{
    private OrderLine(OrderLineId id, ProductSnapshot product, int quantity, Money unitPrice)
    {
        Id = id;
        Product = product;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public OrderLineId Id { get; }

    public ProductSnapshot Product { get; }

    public int Quantity { get; }

    public Money UnitPrice { get; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);

    public static OrderLine Create(
        OrderLineId id,
        ProductSnapshot product,
        int quantity,
        Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Order line quantity must be positive.");
        }

        return new OrderLine(id, product, quantity, unitPrice);
    }
}
