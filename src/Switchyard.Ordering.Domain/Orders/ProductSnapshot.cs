namespace Switchyard.Ordering.Domain.Orders;

public sealed record ProductSnapshot
{
    public ProductSnapshot(SkuCode skuCode, string productName)
    {
        ArgumentNullException.ThrowIfNull(skuCode);

        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new ArgumentException("Product name is required.", nameof(productName));
        }

        SkuCode = skuCode;
        ProductName = productName.Trim();
    }

    public SkuCode SkuCode { get; }

    public string ProductName { get; }
}
