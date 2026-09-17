namespace Switchyard.Ordering.Infrastructure.Persistence;

internal sealed class OrderLineRecord
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public int Position { get; set; }

    public string SkuCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPriceAmount { get; set; }

    public string Currency { get; set; } = string.Empty;
}
