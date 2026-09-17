using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Ordering.Application.Tests;

public sealed class GetOrderHandlerTests
{
    [Fact]
    public async Task ReturnsOrderDetailsWhenOrderExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var order = Order.Create(OrderId.New(), new OrderNumber("SW-00100000"),
                                 new[]
                                 {
                                     OrderLine.Create(OrderLineId.New(),
                                                      new ProductSnapshot(new SkuCode("BIKE-001"), "Road Bike"),
                                                      2, Money.Gbp(499.99m))
                                 },
                                 new DateTimeOffset(2026, 9, 17, 7, 30, 0, TimeSpan.Zero));

        var handler = new GetOrderHandler(new StubOrderRepository(order));

        var result = await handler.HandleAsync(order.Id.Value, cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(order.Id.Value, result!.OrderId);
        Assert.Equal("SW-00100000", result.OrderNumber);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(999.98m, result.TotalAmount);
        Assert.Equal("GBP", result.Currency);
        Assert.Single(result.Lines);
        Assert.Equal("BIKE-001", result.Lines[0].SkuCode);
        Assert.Equal(999.98m, result.Lines[0].LineTotalAmount);
    }

    [Fact]
    public async Task ReturnsNullWhenOrderDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new GetOrderHandler(new StubOrderRepository(null));

        var result = await handler.HandleAsync(Guid.NewGuid(), cancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReturnsNullForEmptyOrderIdWithoutCallingRepository()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = new StubOrderRepository(null);
        var handler = new GetOrderHandler(repository);

        var result = await handler.HandleAsync(Guid.Empty, cancellationToken);

        Assert.Null(result);
        Assert.Equal(0, repository.GetCount);
    }

    private sealed class StubOrderRepository : IOrderRepository
    {
        private readonly Order? _order;

        public StubOrderRepository(Order? order)
        {
            _order = order;
        }

        public int GetCount { get; private set; }

        public Task AddAsync(Order order, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult(_order);
        }
    }
}
