namespace Switchyard.Ordering.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void ConstructorRejectsNegativeAmount()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-0.01m, "GBP"));

        Assert.Equal("amount", exception.ParamName);
    }

    [Fact]
    public void ConstructorNormalizesCurrencyCode()
    {
        var money = new Money(12.50m, " gbp ");

        Assert.Equal(12.50m, money.Amount);
        Assert.Equal("GBP", money.Currency);
    }

    [Fact]
    public void AddRejectsDifferentCurrencies()
    {
        var pounds = new Money(10m, "GBP");
        var euros = new Money(5m, "EUR");

        Assert.Throws<InvalidOperationException>(() => _ = pounds + euros);
    }

    [Fact]
    public void MultiplyRejectsNonPositiveQuantity()
    {
        var price = Money.Gbp(10m);

        Assert.Throws<ArgumentOutOfRangeException>(() => price.Multiply(0));
    }
}
