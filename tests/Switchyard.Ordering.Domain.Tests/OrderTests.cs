namespace Switchyard.Ordering.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateRequiresAtLeastOneLine()
    {
        Assert.Throws<ArgumentException>(() => Order.Create(
            OrderId.New(),
            new OrderNumber("SW-100001"),
            Array.Empty<OrderLine>(),
            CreatedAtUtc));
    }

    [Fact]
    public void CreateStartsPendingAndCalculatesTotal()
    {
        var order = Order.Create(
            OrderId.New(),
            new OrderNumber("SW-100001"),
            new[]
            {
                CreateLine("SKU-001", "Trail Bike", 1, 1299m),
                CreateLine("SKU-002", "Helmet", 2, 75m)
            },
            CreatedAtUtc);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1449m, order.Total.Amount);
        Assert.Equal("GBP", order.Total.Currency);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(CreatedAtUtc, order.CreatedAtUtc);
    }

    [Fact]
    public void CreateRejectsMixedCurrencies()
    {
        var firstLine = CreateLine("SKU-001", "Trail Bike", 1, 1299m);
        var secondLine = OrderLine.Create(
            OrderLineId.New(),
            new ProductSnapshot(new SkuCode("SKU-002"), "Helmet"),
            1,
            new Money(75m, "EUR"));

        Assert.Throws<ArgumentException>(() => Order.Create(
            OrderId.New(),
            new OrderNumber("SW-100001"),
            new[] { firstLine, secondLine },
            CreatedAtUtc));
    }

    [Fact]
    public void CreateRejectsDuplicateLineIdentity()
    {
        var lineId = OrderLineId.New();
        var firstLine = OrderLine.Create(
            lineId,
            new ProductSnapshot(new SkuCode("SKU-001"), "Trail Bike"),
            1,
            Money.Gbp(1299m));
        var secondLine = OrderLine.Create(
            lineId,
            new ProductSnapshot(new SkuCode("SKU-002"), "Helmet"),
            1,
            Money.Gbp(75m));

        Assert.Throws<ArgumentException>(() => Order.Create(
            OrderId.New(),
            new OrderNumber("SW-100001"),
            new[] { firstLine, secondLine },
            CreatedAtUtc));
    }

    [Fact]
    public void ConfirmTransitionsPendingOrderToConfirmed()
    {
        var order = CreateOrder();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void FailTransitionsPendingOrderToFailed()
    {
        var order = CreateOrder();

        order.Fail();

        Assert.Equal(OrderStatus.Failed, order.Status);
    }

    [Fact]
    public void TerminalTransitionCannotBeOverwritten()
    {
        var order = CreateOrder();
        order.Confirm();

        Assert.Throws<InvalidOperationException>(order.Fail);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    private static Order CreateOrder() => Order.Create(
        OrderId.New(),
        new OrderNumber("SW-100001"),
        new[] { CreateLine("SKU-001", "Trail Bike", 1, 1299m) },
        CreatedAtUtc);

    private static OrderLine CreateLine(
        string skuCode,
        string productName,
        int quantity,
        decimal unitPrice) => OrderLine.Create(
            OrderLineId.New(),
            new ProductSnapshot(new SkuCode(skuCode), productName),
            quantity,
            Money.Gbp(unitPrice));
}
