using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Orders;

public sealed class CreatePendingOrderHandler
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly IOrderNumberGenerator _orderNumberGenerator;
    private readonly TimeProvider _timeProvider;

    public CreatePendingOrderHandler(
        IOrderRepository orderRepository,
        IOrderingUnitOfWork unitOfWork,
        IOrderNumberGenerator orderNumberGenerator,
        TimeProvider timeProvider)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _orderNumberGenerator = orderNumberGenerator ?? throw new ArgumentNullException(nameof(orderNumberGenerator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CreatePendingOrderResult> HandleAsync(
        CreatePendingOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        var orderNumber = await _orderNumberGenerator.NextAsync(cancellationToken);
        var lines = command.Lines
            .Select(line => OrderLine.Create(
                OrderLineId.New(),
                new ProductSnapshot(new SkuCode(line.SkuCode), line.ProductName),
                line.Quantity,
                new Money(line.UnitPriceAmount, line.Currency)))
            .ToArray();

        var order = Order.Create(
            OrderId.New(),
            orderNumber,
            lines,
            _timeProvider.GetUtcNow());

        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatePendingOrderResult(
            order.Id.Value,
            order.OrderNumber.Value,
            order.Status.ToString(),
            order.Total.Amount,
            order.Total.Currency,
            order.CreatedAtUtc);
    }
}
