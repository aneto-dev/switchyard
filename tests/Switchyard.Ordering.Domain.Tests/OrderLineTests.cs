namespace Switchyard.Ordering.Domain.Tests;

public sealed class OrderLineTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateRejectsNonPositiveQuantity(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderLine.Create(
            OrderLineId.New(),
            new ProductSnapshot(new SkuCode("SKU-001"), "Trail Bike"),
            quantity,
            Money.Gbp(1000m)));
    }

    [Fact]
    public void LineTotalUsesImmutableOrderTimeUnitPrice()
    {
        var line = OrderLine.Create(
            OrderLineId.New(),
            new ProductSnapshot(new SkuCode("SKU-001"), "Trail Bike"),
            2,
            Money.Gbp(749.50m));

        Assert.Equal(1499m, line.LineTotal.Amount);
        Assert.Equal("GBP", line.LineTotal.Currency);
        Assert.Equal(749.50m, line.UnitPrice.Amount);
        Assert.Equal("Trail Bike", line.Product.ProductName);
    }
}
