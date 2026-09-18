namespace Switchyard.Inventory.Domain.Stock;

public sealed record InventorySku
{
    public InventorySku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SKU code is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
