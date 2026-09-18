using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Orders;

public sealed class CreatePendingOrderHandler
{
    private const int MaxIdempotencyKeyLength = 128;

    private readonly IOrderRepository _orderRepository;
    private readonly IOrderRequestRepository _orderRequestRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly TimeProvider _timeProvider;

    public CreatePendingOrderHandler(
        IOrderRepository orderRepository, IOrderRequestRepository orderRequestRepository,
        IOrderingUnitOfWork unitOfWork, IOrderNumberGenerator orderNumberGenerator,
        TimeProvider timeProvider)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _orderRequestRepository = orderRequestRepository ?? throw new ArgumentNullException(nameof(orderRequestRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _orderNumberGenerator = orderNumberGenerator ?? throw new ArgumentNullException(nameof(orderNumberGenerator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CreatePendingOrderResult> HandleAsync(
        CreatePendingOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        var idempotencyKey = NormalizeIdempotencyKey(command.IdempotencyKey);
        var requestFingerprint = OrderRequestFingerprint.Calculate(command.Lines);
        var existingRequest = await _orderRequestRepository.GetByIdempotencyKeyAsync(
            idempotencyKey, cancellationToken);

        if (existingRequest is not null)
        {
            return await ResolveExistingRequestAsync(
                existingRequest, requestFingerprint, cancellationToken);
        }

        var orderNumber = await _orderNumberGenerator.NextAsync(cancellationToken);
        var lines = command.Lines.Select(line => OrderLine.Create(
                                              OrderLineId.New(),
                                              new ProductSnapshot(new SkuCode(line.SkuCode), line.ProductName),
                                              line.Quantity,
                                              new Money(line.UnitPriceAmount, line.Currency)))
                                 .ToArray();
        var acceptedAtUtc = _timeProvider.GetUtcNow();
        var order = Order.Create(OrderId.New(), orderNumber, lines, acceptedAtUtc);
        var acceptedRequest = new AcceptedOrderRequest(
            idempotencyKey, requestFingerprint, order.Id, acceptedAtUtc);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _orderRequestRepository.AddAsync(acceptedRequest, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateOrderRequestException)
        {
            var concurrentRequest = await _orderRequestRepository.GetByIdempotencyKeyAsync(
                idempotencyKey, cancellationToken);

            if (concurrentRequest is null)
            {
                throw new InvalidOperationException(
                    "The duplicate order request could not be loaded after the idempotency conflict.");
            }

            return await ResolveExistingRequestAsync(
                concurrentRequest, requestFingerprint, cancellationToken);
        }

        return ToResult(order, replayed: false);
    }

    private async Task<CreatePendingOrderResult> ResolveExistingRequestAsync(
        AcceptedOrderRequest existingRequest, string requestFingerprint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                existingRequest.RequestFingerprint,
                requestFingerprint,
                StringComparison.Ordinal))
        {
            throw new OrderRequestConflictException(existingRequest.IdempotencyKey);
        }

        var order = await _orderRepository.GetByIdAsync(existingRequest.OrderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException(
                $"Accepted order request '{existingRequest.IdempotencyKey}' points to a missing order.");
        }

        return ToResult(order, replayed: true);
    }

    private static CreatePendingOrderResult ToResult(Order order, bool replayed)
    {
        return new CreatePendingOrderResult(
            order.Id.Value,
            order.OrderNumber.Value,
            order.Status.ToString(),
            order.Total.Amount,
            order.Total.Currency,
            order.CreatedAtUtc,
            replayed);
    }

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        var normalized = idempotencyKey.Trim();

        if (normalized.Length > MaxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                $"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.",
                nameof(idempotencyKey));
        }

        return normalized;
    }
}
