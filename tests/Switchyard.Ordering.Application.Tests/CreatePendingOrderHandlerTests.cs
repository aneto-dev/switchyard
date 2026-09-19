using System.Text.Json;
using Switchyard.Messaging;
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
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(new OrderNumber("SW-00100000"));
        var now = new DateTimeOffset(2026, 9, 16, 19, 45, 0, TimeSpan.Zero);
        var handler = CreateHandler(
            orderRepository, requestRepository, unitOfWork, outboxWriter, numberGenerator, now);

        var result = await handler.HandleAsync(
            CreateCommand("checkout-001", quantity: 2),
            cancellationToken);

        Assert.NotNull(orderRepository.AddedOrder);
        Assert.Equal(OrderStatus.Pending, orderRepository.AddedOrder!.Status);
        Assert.Equal("SW-00100000", orderRepository.AddedOrder.OrderNumber.Value);
        Assert.Equal(now, orderRepository.AddedOrder.CreatedAtUtc);
        Assert.Equal("BIKE-001", orderRepository.AddedOrder.Lines[0].Product.SkuCode.Value);
        Assert.Equal("Road Bike", orderRepository.AddedOrder.Lines[0].Product.ProductName);
        Assert.Equal(2, orderRepository.AddedOrder.Lines[0].Quantity);
        Assert.Equal(499.99m, orderRepository.AddedOrder.Lines[0].UnitPrice.Amount);
        Assert.Equal("GBP", orderRepository.AddedOrder.Lines[0].UnitPrice.Currency);
        Assert.Equal(999.98m, orderRepository.AddedOrder.Total.Amount);
        Assert.NotNull(requestRepository.AcceptedRequest);
        Assert.Equal("checkout-001", requestRepository.AcceptedRequest!.IdempotencyKey);
        Assert.Equal(orderRepository.AddedOrder.Id, requestRepository.AcceptedRequest.OrderId);
        Assert.Equal(64, requestRepository.AcceptedRequest.RequestFingerprint.Length);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, numberGenerator.CallCount);

        var outboxMessage = Assert.Single(outboxWriter.Messages);
        Assert.Equal(OrderAcceptedIntegrationMessageV1.MessageType, outboxMessage.MessageType);
        Assert.Equal(orderRepository.AddedOrder.Id.Value, outboxMessage.CorrelationId);
        Assert.Null(outboxMessage.CausationId);
        Assert.Equal(now, outboxMessage.OccurredAtUtc);

        var payload = JsonSerializer.Deserialize<OrderAcceptedIntegrationMessageV1>(
            outboxMessage.PayloadJson,
            JsonSerializerOptions.Web);

        Assert.NotNull(payload);
        Assert.Equal(orderRepository.AddedOrder.Id.Value, payload.OrderId);
        Assert.Equal("SW-00100000", payload.OrderNumber);
        Assert.Equal(999.98m, payload.TotalAmount);
        Assert.Equal("GBP", payload.Currency);

        Assert.Equal(orderRepository.AddedOrder.Id.Value, result.OrderId);
        Assert.Equal("SW-00100000", result.OrderNumber);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(999.98m, result.TotalAmount);
        Assert.Equal("GBP", result.Currency);
        Assert.Equal(now, result.CreatedAtUtc);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task ReplaysSameRequestWithoutCreatingAnotherOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(new OrderNumber("SW-00100000"));
        var handler = CreateHandler(
            orderRepository, requestRepository, unitOfWork, outboxWriter, numberGenerator, DateTimeOffset.UnixEpoch);
        var command = CreateCommand("checkout-retry");

        var created = await handler.HandleAsync(command, cancellationToken);
        var replayed = await handler.HandleAsync(command, cancellationToken);

        Assert.Equal(created.OrderId, replayed.OrderId);
        Assert.Equal(created.OrderNumber, replayed.OrderNumber);
        Assert.True(replayed.Replayed);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, numberGenerator.CallCount);
        Assert.Equal(1, orderRepository.AddCount);
        Assert.Equal(1, requestRepository.AddCount);
        Assert.Single(outboxWriter.Messages);
    }

    [Fact]
    public async Task RejectsDifferentRequestUsingSameIdempotencyKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(new OrderNumber("SW-00100000"));
        var handler = CreateHandler(
            orderRepository, requestRepository, unitOfWork, outboxWriter, numberGenerator, DateTimeOffset.UnixEpoch);

        await handler.HandleAsync(CreateCommand("checkout-conflict"), cancellationToken);

        await Assert.ThrowsAsync<OrderRequestConflictException>(
            () => handler.HandleAsync(
                CreateCommand("checkout-conflict", quantity: 3),
                cancellationToken));

        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, numberGenerator.CallCount);
        Assert.Equal(1, orderRepository.AddCount);
        Assert.Equal(1, requestRepository.AddCount);
        Assert.Single(outboxWriter.Messages);
    }

    [Fact]
    public async Task RejectsInvalidLineBeforePersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(new OrderNumber("SW-00100001"));
        var handler = CreateHandler(
            orderRepository, requestRepository, unitOfWork, outboxWriter, numberGenerator, DateTimeOffset.UnixEpoch);

        var command = new CreatePendingOrderCommand(
            "checkout-invalid",
            new[]
            {
                new CreatePendingOrderLine("BIKE-001", "Road Bike", 0, 499.99m, "GBP")
            });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(command, cancellationToken));

        Assert.Null(orderRepository.AddedOrder);
        Assert.Null(requestRepository.AcceptedRequest);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Equal(0, numberGenerator.CallCount);
        Assert.Empty(outboxWriter.Messages);
    }

    private static CreatePendingOrderHandler CreateHandler(
        RecordingOrderRepository orderRepository,
        RecordingOrderRequestRepository requestRepository,
        RecordingUnitOfWork unitOfWork,
        RecordingOutboxWriter outboxWriter,
        FixedOrderNumberGenerator numberGenerator,
        DateTimeOffset now)
    {
        return new CreatePendingOrderHandler(
            orderRepository,
            requestRepository,
            unitOfWork,
            outboxWriter,
            numberGenerator,
            new FixedTimeProvider(now));
    }

    private static CreatePendingOrderCommand CreateCommand(string idempotencyKey, int quantity = 1)
    {
        return new CreatePendingOrderCommand(
            idempotencyKey,
            new[]
            {
                new CreatePendingOrderLine("BIKE-001", "Road Bike", quantity, 499.99m, "gbp")
            });
    }

    private sealed class RecordingOrderRepository : IOrderRepository
    {
        public Order? AddedOrder { get; private set; }

        public int AddCount { get; private set; }

        public Task AddAsync(Order order, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedOrder = order;
            AddCount++;
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

    private sealed class RecordingOrderRequestRepository : IOrderRequestRepository
    {
        public AcceptedOrderRequest? AcceptedRequest { get; private set; }

        public int AddCount { get; private set; }

        public Task<AcceptedOrderRequest?> GetByIdempotencyKeyAsync(
            string idempotencyKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                AcceptedRequest?.IdempotencyKey == idempotencyKey
                    ? AcceptedRequest
                    : null);
        }

        public Task AddAsync(AcceptedOrderRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcceptedRequest = request;
            AddCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutboxWriter : IOutboxWriter
    {
        public List<IntegrationMessageEnvelope> Messages { get; } = [];

        public Task AddAsync(
            IntegrationMessageEnvelope message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.CompletedTask;
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

        public int CallCount { get; private set; }

        public Task<OrderNumber> NextAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
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
