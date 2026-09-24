using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Ordering.Application.Tests;

public sealed class CreatePendingOrderHandlerTests
{
    [Fact]
    public async Task CreatesPendingOrderProcessAndInventoryReservationCommand()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var processRepository = new RecordingPlacementProcessRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(
            new OrderNumber("SW-00100000"));
        var now = new DateTimeOffset(
            2026, 9, 16, 19, 45, 0, TimeSpan.Zero);
        var handler = CreateHandler(
            orderRepository,
            requestRepository,
            processRepository,
            unitOfWork,
            outboxWriter,
            numberGenerator,
            now);

        var result = await handler.HandleAsync(
            CreateCommand("checkout-001", quantity: 2),
            cancellationToken);

        var order = Assert.IsType<Order>(orderRepository.AddedOrder);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("SW-00100000", order.OrderNumber.Value);
        Assert.Equal(now, order.CreatedAtUtc);
        Assert.Equal("BIKE-001", order.Lines[0].Product.SkuCode.Value);
        Assert.Equal("Road Bike", order.Lines[0].Product.ProductName);
        Assert.Equal(2, order.Lines[0].Quantity);
        Assert.Equal(499.99m, order.Lines[0].UnitPrice.Amount);
        Assert.Equal("GBP", order.Lines[0].UnitPrice.Currency);
        Assert.Equal(999.98m, order.Total.Amount);

        Assert.NotNull(requestRepository.AcceptedRequest);
        Assert.Equal(
            "checkout-001",
            requestRepository.AcceptedRequest!.IdempotencyKey);
        Assert.Equal(
            order.Id,
            requestRepository.AcceptedRequest.OrderId);
        Assert.Equal(
            64,
            requestRepository.AcceptedRequest.RequestFingerprint.Length);

        var process = Assert.IsType<OrderPlacementProcess>(
            processRepository.AddedProcess);
        Assert.Equal(order.Id, process.OrderId);
        Assert.Equal(OrderPlacementState.AwaitingInventory, process.State);
        Assert.Equal(now, process.StartedAtUtc);
        Assert.Equal(now, process.UpdatedAtUtc);

        var placementLine = Assert.Single(process.Lines);
        Assert.Equal(order.Lines[0].Id, placementLine.OrderLineId);
        Assert.NotEqual(Guid.Empty, placementLine.ReservationRequestId);
        Assert.Equal(
            OrderPlacementLineState.AwaitingReservation,
            placementLine.State);
        Assert.Null(placementLine.ReservationId);

        Assert.Equal(2, outboxWriter.Messages.Count);

        var accepted = Assert.Single(
            outboxWriter.Messages,
            message =>
                message.MessageType ==
                OrderAcceptedIntegrationMessageV1.MessageType);
        var reserve = Assert.Single(
            outboxWriter.Messages,
            message =>
                message.MessageType ==
                ReserveInventoryV1.MessageType);

        Assert.Equal(order.Id.Value, accepted.CorrelationId);
        Assert.Null(accepted.CausationId);
        Assert.Equal(now, accepted.OccurredAtUtc);

        var acceptedPayload =
            JsonSerializer.Deserialize<OrderAcceptedIntegrationMessageV1>(
                accepted.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(acceptedPayload);
        Assert.Equal(order.Id.Value, acceptedPayload.OrderId);
        Assert.Equal("SW-00100000", acceptedPayload.OrderNumber);
        Assert.Equal(999.98m, acceptedPayload.TotalAmount);
        Assert.Equal("GBP", acceptedPayload.Currency);

        Assert.Equal(order.Id.Value, reserve.CorrelationId);
        Assert.Equal(accepted.MessageId, reserve.CausationId);
        Assert.Equal(now, reserve.OccurredAtUtc);

        var reservePayload =
            JsonSerializer.Deserialize<ReserveInventoryV1>(
                reserve.PayloadJson,
                JsonSerializerOptions.Web);

        Assert.NotNull(reservePayload);
        Assert.Equal(
            placementLine.ReservationRequestId,
            reservePayload.RequestId);
        Assert.Equal(order.Id.Value, reservePayload.OrderId);
        Assert.Equal(order.Lines[0].Id.Value, reservePayload.OrderLineId);
        Assert.Equal("BIKE-001", reservePayload.SkuCode);
        Assert.Equal(2, reservePayload.Quantity);

        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, numberGenerator.CallCount);

        Assert.Equal(order.Id.Value, result.OrderId);
        Assert.Equal("SW-00100000", result.OrderNumber);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(999.98m, result.TotalAmount);
        Assert.Equal("GBP", result.Currency);
        Assert.Equal(now, result.CreatedAtUtc);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task ReplaysSameRequestWithoutCreatingAnotherProcessOrMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var processRepository = new RecordingPlacementProcessRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(
            new OrderNumber("SW-00100000"));
        var handler = CreateHandler(
            orderRepository,
            requestRepository,
            processRepository,
            unitOfWork,
            outboxWriter,
            numberGenerator,
            DateTimeOffset.UnixEpoch);
        var command = CreateCommand("checkout-retry");

        var created = await handler.HandleAsync(command, cancellationToken);
        var replayed = await handler.HandleAsync(command, cancellationToken);

        Assert.Equal(created.OrderId, replayed.OrderId);
        Assert.Equal(created.OrderNumber, replayed.OrderNumber);
        Assert.True(replayed.Replayed);
        Assert.Equal(1, orderRepository.AddCount);
        Assert.Equal(1, requestRepository.AddCount);
        Assert.Equal(1, processRepository.AddCount);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, numberGenerator.CallCount);
        Assert.Equal(2, outboxWriter.Messages.Count);
    }

    [Fact]
    public async Task RejectsDifferentRequestUsingSameIdempotencyKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var processRepository = new RecordingPlacementProcessRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(
            new OrderNumber("SW-00100000"));
        var handler = CreateHandler(
            orderRepository,
            requestRepository,
            processRepository,
            unitOfWork,
            outboxWriter,
            numberGenerator,
            DateTimeOffset.UnixEpoch);

        await handler.HandleAsync(
            CreateCommand("checkout-conflict"),
            cancellationToken);

        await Assert.ThrowsAsync<OrderRequestConflictException>(
            () => handler.HandleAsync(
                CreateCommand("checkout-conflict", quantity: 3),
                cancellationToken));

        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, numberGenerator.CallCount);
        Assert.Equal(1, orderRepository.AddCount);
        Assert.Equal(1, requestRepository.AddCount);
        Assert.Equal(1, processRepository.AddCount);
        Assert.Equal(2, outboxWriter.Messages.Count);
    }

    [Fact]
    public async Task RejectsInvalidLineBeforePersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orderRepository = new RecordingOrderRepository();
        var requestRepository = new RecordingOrderRequestRepository();
        var processRepository = new RecordingPlacementProcessRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var outboxWriter = new RecordingOutboxWriter();
        var numberGenerator = new FixedOrderNumberGenerator(
            new OrderNumber("SW-00100001"));
        var handler = CreateHandler(
            orderRepository,
            requestRepository,
            processRepository,
            unitOfWork,
            outboxWriter,
            numberGenerator,
            DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(
                new CreatePendingOrderCommand(
                    "checkout-invalid",
                    new[]
                    {
                        new CreatePendingOrderLine(
                            "BIKE-001", "Road Bike", 0, 499.99m, "GBP")
                    }),
                cancellationToken));

        Assert.Null(orderRepository.AddedOrder);
        Assert.Null(requestRepository.AcceptedRequest);
        Assert.Null(processRepository.AddedProcess);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Equal(0, numberGenerator.CallCount);
        Assert.Empty(outboxWriter.Messages);
    }

    private static CreatePendingOrderHandler CreateHandler(
        RecordingOrderRepository orderRepository,
        RecordingOrderRequestRepository requestRepository,
        RecordingPlacementProcessRepository processRepository,
        RecordingUnitOfWork unitOfWork,
        RecordingOutboxWriter outboxWriter,
        FixedOrderNumberGenerator numberGenerator,
        DateTimeOffset now) =>
        new(
            orderRepository,
            requestRepository,
            processRepository,
            unitOfWork,
            outboxWriter,
            numberGenerator,
            new FixedTimeProvider(now));

    private static CreatePendingOrderCommand CreateCommand(
        string idempotencyKey,
        int quantity = 1) =>
        new(
            idempotencyKey,
            new[]
            {
                new CreatePendingOrderLine(
                    "BIKE-001", "Road Bike", quantity, 499.99m, "gbp")
            });

    private sealed class RecordingOrderRepository : IOrderRepository
    {
        public Order? AddedOrder { get; private set; }
        public int AddCount { get; private set; }

        public Task AddAsync(
            Order order,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedOrder = order;
            AddCount++;
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(
            OrderId orderId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                AddedOrder?.Id == orderId
                    ? AddedOrder
                    : null);
        }
    }

    private sealed class RecordingOrderRequestRepository :
        IOrderRequestRepository
    {
        public AcceptedOrderRequest? AcceptedRequest { get; private set; }
        public int AddCount { get; private set; }

        public Task<AcceptedOrderRequest?> GetByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                AcceptedRequest?.IdempotencyKey == idempotencyKey
                    ? AcceptedRequest
                    : null);
        }

        public Task AddAsync(
            AcceptedOrderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcceptedRequest = request;
            AddCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlacementProcessRepository :
        IOrderPlacementProcessRepository
    {
        public OrderPlacementProcess? AddedProcess { get; private set; }
        public int AddCount { get; private set; }

        public Task AddAsync(
            OrderPlacementProcess process,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedProcess = process;
            AddCount++;
            return Task.CompletedTask;
        }

        public Task<OrderPlacementProcess?> GetByOrderIdAsync(
            OrderId orderId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                AddedProcess?.OrderId == orderId
                    ? AddedProcess
                    : null);
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

        public Task<OrderNumber> NextAsync(
            CancellationToken cancellationToken)
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
