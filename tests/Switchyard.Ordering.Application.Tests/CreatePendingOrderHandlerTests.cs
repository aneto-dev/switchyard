using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Ordering.Application.Tests;

public sealed class CreatePendingOrderHandlerTests
{
    [Fact]
    public async Task CreatesAndPersistsPendingOrderFromOrderTimeSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = new RecordingOrderRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var now = new DateTimeOffset(2026, 9, 16, 19, 45, 0, TimeSpan.Zero);
        var handler = new CreatePendingOrderHandler(
            repository,
            unitOfWork,
            new FixedOrderNumberGenerator(new OrderNumber("SW-00100000")),
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new CreatePendingOrderCommand(
                new[]
                {
                    new CreatePendingOrderLine("BIKE-001", "Road Bike", 2, 499.99m, "gbp")
                }),
            cancellationToken);

        Assert.NotNull(repository.AddedOrder);
        Assert.Equal(OrderStatus.Pending, repository.AddedOrder!.Status);
        Assert.Equal("SW-00100000", repository.AddedOrder.OrderNumber.Value);
        Assert.Equal(now, repository.AddedOrder.CreatedAtUtc);
        Assert.Equal("BIKE-001", repository.AddedOrder.Lines[0].Product.SkuCode.Value);
        Assert.Equal("Road Bike", repository.AddedOrder.Lines[0].Product.ProductName);
        Assert.Equal(2, repository.AddedOrder.Lines[0].Quantity);
        Assert.Equal(499.99m, repository.AddedOrder.Lines[0].UnitPrice.Amount);
        Assert.Equal("GBP", repository.AddedOrder.Lines[0].UnitPrice.Currency);
        Assert.Equal(999.98m, repository.AddedOrder.Total.Amount);
        Assert.Equal(1, unitOfWork.SaveCount);

        Assert.Equal(repository.AddedOrder.Id.Value, result.OrderId);
        Assert.Equal("SW-00100000", result.OrderNumber);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(999.98m, result.TotalAmount);
        Assert.Equal("GBP", result.Currency);
        Assert.Equal(now, result.CreatedAtUtc);
    }

    [Fact]
    public async Task RejectsInvalidLineBeforePersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = new RecordingOrderRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new CreatePendingOrderHandler(
            repository,
            unitOfWork,
            new FixedOrderNumberGenerator(new OrderNumber("SW-00100001")),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var command = new CreatePendingOrderCommand(
            new[]
            {
                new CreatePendingOrderLine("BIKE-001", "Road Bike", 0, 499.99m, "GBP")
            });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(command, cancellationToken));

        Assert.Null(repository.AddedOrder);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private sealed class RecordingOrderRepository : IOrderRepository
    {
        public Order? AddedOrder { get; private set; }

        public Task AddAsync(Order order, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedOrder = order;
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(OrderId orderId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                AddedOrder?.Id == orderId
                    ? AddedOrder
                    : null);
        }
    }

    private sealed class RecordingUnitOfWork : IOrderingUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedOrderNumberGenerator : IOrderNumberGenerator
    {
        private readonly OrderNumber _orderNumber;

        public FixedOrderNumberGenerator(OrderNumber orderNumber)
        {
            _orderNumber = orderNumber;
        }

        public Task<OrderNumber> NextAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_orderNumber);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
