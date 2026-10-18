using System.Text.Json;
using Switchyard.IntegrationContracts.Inventory;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderingPaymentOutcomeHandler
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderPlacementProcessRepository _processRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly TimeProvider _timeProvider;

    public OrderingPaymentOutcomeHandler(
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

    public async Task HandleAuthorisedAsync(
        PaymentAuthorisedV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureCausationId(causationId);

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var order =
            await LoadOrderAsync(
                process.OrderId,
                cancellationToken);

        EnsureAmountMatches(
            order,
            outcome.Amount,
            outcome.Currency);

        var transition =
            process.RecordPaymentAuthorised(
                outcome.RequestId,
                _timeProvider.GetUtcNow());

        if (!transition.Changed)
        {
            if (order.Status != OrderStatus.Confirmed)
            {
                throw new OrderPlacementPaymentOutcomeException(
                    $"Confirmed placement '{process.OrderId.Value}' has order status '{order.Status}'.");
            }

            return;
        }

        await _processRepository.UpdateAsync(
            process,
            cancellationToken);

        if (!transition.ConfirmOrder)
        {
            throw new OrderPlacementPaymentOutcomeException(
                "Payment-authorised transition did not request order confirmation.");
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Order '{order.Id.Value}' cannot confirm from status '{order.Status}'.");
        }

        order.Confirm();

        await _orderRepository.UpdateAsync(
            order,
            cancellationToken);
    }

    public async Task HandleFailedAsync(
        PaymentAuthorisationFailedV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureCausationId(causationId);

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var order =
            await LoadOrderAsync(
                process.OrderId,
                cancellationToken);

        EnsureAmountMatches(
            order,
            outcome.Amount,
            outcome.Currency);

        var transition =
            process.RecordPaymentFailed(
                outcome.RequestId,
                _timeProvider.GetUtcNow());

        if (!transition.Changed)
        {
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Order '{order.Id.Value}' cannot enter payment compensation from status '{order.Status}'.");
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
    }

    public async Task HandleIndeterminateAsync(
        PaymentAuthorisationIndeterminateV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureCausationId(causationId);

        var process =
            await LoadProcessForUpdateAsync(
                outcome.OrderId,
                cancellationToken);

        var order =
            await LoadOrderAsync(
                process.OrderId,
                cancellationToken);

        EnsureAmountMatches(
            order,
            outcome.Amount,
            outcome.Currency);

        var transition =
            process.RecordPaymentIndeterminate(
                outcome.RequestId,
                _timeProvider.GetUtcNow());

        if (!transition.Changed)
        {
            if (order.Status != OrderStatus.Pending)
            {
                throw new OrderPlacementPaymentOutcomeException(
                    $"Indeterminate placement '{process.OrderId.Value}' has order status '{order.Status}'.");
            }

            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Order '{order.Id.Value}' cannot become payment-indeterminate from status '{order.Status}'.");
        }

        await _processRepository.UpdateAsync(
            process,
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
               throw new OrderPlacementPaymentOutcomeException(
                   $"Order-placement process '{orderId}' does not exist.");
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
               throw new OrderPlacementPaymentOutcomeException(
                   $"Order '{orderId.Value}' does not exist.");
    }

    private static void EnsureAmountMatches(
        Order order,
        decimal amount,
        string currency)
    {
        if (order.Total.Amount != amount ||
            !string.Equals(
                order.Total.Currency,
                currency,
                StringComparison.Ordinal))
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Payment outcome amount does not match order '{order.Id.Value}'.");
        }
    }

    private static void EnsureCausationId(Guid causationId)
    {
        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty.",
                nameof(causationId));
        }
    }
}
