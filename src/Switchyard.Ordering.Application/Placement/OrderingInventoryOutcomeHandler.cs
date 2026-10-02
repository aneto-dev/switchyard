using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderingInventoryOutcomeHandler
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderPlacementProcessRepository _processRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly TimeProvider _timeProvider;

    public OrderingInventoryOutcomeHandler(
        IOrderRepository orderRepository,
        IOrderPlacementProcessRepository processRepository,
        IOutboxWriter outboxWriter,
        TimeProvider timeProvider)
    {
        _orderRepository =
            orderRepository ??
            throw new ArgumentNullException(nameof(orderRepository));

        _processRepository =
            processRepository ??
            throw new ArgumentNullException(nameof(processRepository));

        _outboxWriter =
            outboxWriter ??
            throw new ArgumentNullException(nameof(outboxWriter));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task HandleReservedAsync(
        InventoryReservedV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty.",
                nameof(causationId));
        }

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var transition =
            process.RecordInventoryReserved(
                outcome.RequestId,
                new OrderLineId(outcome.OrderLineId),
                outcome.SkuCode,
                outcome.Quantity,
                outcome.ReservationId,
                Guid.NewGuid(),
                _timeProvider.GetUtcNow());

        await ApplyTransitionAsync(
            process,
            transition,
            causationId,
            cancellationToken);
    }

    public async Task HandleRejectedAsync(
        InventoryRejectedV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty.",
                nameof(causationId));
        }

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var transition =
            process.RecordInventoryRejected(
                outcome.RequestId,
                new OrderLineId(outcome.OrderLineId),
                outcome.SkuCode,
                outcome.Quantity,
                _timeProvider.GetUtcNow());

        await ApplyTransitionAsync(
            process,
            transition,
            causationId,
            cancellationToken);
    }

    public async Task HandleReleasedAsync(
        InventoryReleasedV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty.",
                nameof(causationId));
        }

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var transition =
            process.RecordInventoryReleased(
                outcome.RequestId,
                outcome.ReservationId,
                _timeProvider.GetUtcNow());

        await ApplyTransitionAsync(
            process,
            transition,
            causationId,
            cancellationToken);
    }

    public async Task HandleExpiredAsync(
        InventoryExpiredV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty.",
                nameof(causationId));
        }

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var transition =
            process.RecordInventoryExpired(
                outcome.RequestId,
                outcome.ReservationId,
                _timeProvider.GetUtcNow());

        await ApplyTransitionAsync(
            process,
            transition,
            causationId,
            cancellationToken);
    }

    private async Task<OrderPlacementProcess> LoadProcessForUpdateAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var process =
            await _processRepository.GetByOrderIdForUpdateAsync(
                new OrderId(orderId),
                cancellationToken);

        return process ??
               throw new OrderPlacementInventoryOutcomeException(
                   $"Order-placement process '{orderId}' does not exist.");
    }

    private async Task ApplyTransitionAsync(
        OrderPlacementProcess process,
        OrderPlacementInventoryTransition transition,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        if (!transition.Changed)
        {
            return;
        }

        await _processRepository.UpdateAsync(
            process,
            cancellationToken);

        var now =
            _timeProvider.GetUtcNow();

        foreach (var release in transition.Releases)
        {
            var command =
                new ReleaseInventoryReservationV1(
                    release.ReservationRequestId,
                    process.OrderId.Value,
                    release.ReservationId,
                    ReleaseInventoryReservationV1.CompensationReason);

            await _outboxWriter.AddAsync(
                new IntegrationMessageEnvelope(
                    Guid.NewGuid(),
                    ReleaseInventoryReservationV1.MessageType,
                    JsonSerializer.Serialize(
                        command,
                        JsonSerializerOptions.Web),
                    now,
                    process.OrderId.Value,
                    causationId),
                cancellationToken);
        }

        if (transition.AuthorisePayment)
        {
            var order =
                await LoadOrderAsync(
                    process.OrderId,
                    cancellationToken);

            if (order.Status != OrderStatus.Pending)
            {
                throw new OrderPlacementInventoryOutcomeException(
                    $"Order '{order.Id.Value}' cannot request payment from status '{order.Status}'.");
            }

            var requestId =
                process.PaymentAuthorisationRequestId ??
                throw new OrderPlacementInventoryOutcomeException(
                    "AwaitingPayment process is missing its payment authorisation request ID.");

            var command =
                new AuthorisePaymentV1(
                    requestId,
                    order.Id.Value,
                    order.Total.Amount,
                    order.Total.Currency);

            await _outboxWriter.AddAsync(
                new IntegrationMessageEnvelope(
                    requestId,
                    AuthorisePaymentV1.MessageType,
                    JsonSerializer.Serialize(
                        command,
                        JsonSerializerOptions.Web),
                    now,
                    order.Id.Value,
                    causationId),
                cancellationToken);
        }

        if (transition.FailPlacement)
        {
            var order =
                await LoadOrderAsync(
                    process.OrderId,
                    cancellationToken);

            if (order.Status == OrderStatus.Pending)
            {
                order.Fail();

                await _orderRepository.UpdateAsync(
                    order,
                    cancellationToken);
            }
            else if (order.Status != OrderStatus.Failed)
            {
                throw new OrderPlacementInventoryOutcomeException(
                    $"Order '{order.Id.Value}' cannot fail from status '{order.Status}'.");
            }
        }
    }

    private async Task<Order> LoadOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var order =
            await _orderRepository.GetByIdAsync(
                orderId,
                cancellationToken);

        return order ??
               throw new OrderPlacementInventoryOutcomeException(
                   $"Order '{orderId.Value}' does not exist.");
    }
}
