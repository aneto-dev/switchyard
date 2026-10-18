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
        ArgumentNullException.ThrowIfNull(
            outcome);
        EnsureCausationId(
            causationId);

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
            EnsureOrderMatchesProcess(
                process,
                order);

            return;
        }

        await _processRepository.UpdateAsync(
            process,
            cancellationToken);

        if (transition.ConfirmOrder)
        {
            if (order.Status != OrderStatus.Pending)
            {
                throw new OrderPlacementPaymentOutcomeException(
                    $"Order '{order.Id.Value}' cannot confirm from status '{order.Status}'.");
            }

            order.Confirm();

            await _orderRepository.UpdateAsync(
                order,
                cancellationToken);

            return;
        }

        if (process.State ==
            OrderPlacementState.NeedsAttention)
        {
            if (order.Status != OrderStatus.Pending)
            {
                throw new OrderPlacementPaymentOutcomeException(
                    $"NeedsAttention placement '{process.OrderId.Value}' has order status '{order.Status}'.");
            }

            return;
        }

        throw new OrderPlacementPaymentOutcomeException(
            "Payment-authorised transition produced an unsupported Ordering state.");
    }

    public async Task HandleFailedAsync(
        PaymentAuthorisationFailedV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            outcome);
        EnsureCausationId(
            causationId);

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

        var handledAtUtc =
            _timeProvider.GetUtcNow();

        var transition =
            process.RecordPaymentFailed(
                outcome.RequestId,
                handledAtUtc);

        if (!transition.Changed)
        {
            EnsureOrderMatchesProcess(
                process,
                order);

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

        await StageReleasesAsync(
            process,
            transition.Releases,
            causationId,
            handledAtUtc,
            cancellationToken);

        if (transition.FailOrder)
        {
            order.Fail();

            await _orderRepository.UpdateAsync(
                order,
                cancellationToken);
        }
    }

    public async Task HandleIndeterminateAsync(
        PaymentAuthorisationIndeterminateV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            outcome);
        EnsureCausationId(
            causationId);

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
            EnsureOrderMatchesProcess(
                process,
                order);

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

    public async Task HandleNeedsAttentionAsync(
        PaymentAuthorisationNeedsAttentionV1 outcome,
        Guid causationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            outcome);
        EnsureCausationId(
            causationId);

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
            process.RecordPaymentNeedsAttention(
                outcome.RequestId,
                _timeProvider.GetUtcNow());

        if (!transition.Changed)
        {
            EnsureOrderMatchesProcess(
                process,
                order);

            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Order '{order.Id.Value}' cannot enter NeedsAttention from status '{order.Status}'.");
        }

        await _processRepository.UpdateAsync(
            process,
            cancellationToken);
    }

    private async Task StageReleasesAsync(
        OrderPlacementProcess process,
        IReadOnlyList<OrderPlacementRelease> releases,
        Guid causationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (var release in releases)
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
                    occurredAtUtc,
                    process.OrderId.Value,
                    causationId),
                cancellationToken);
        }
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

    private static void EnsureOrderMatchesProcess(
        OrderPlacementProcess process,
        Order order)
    {
        var expectedStatus =
            process.State switch
            {
                OrderPlacementState.Confirmed =>
                    OrderStatus.Confirmed,
                OrderPlacementState.Failed =>
                    OrderStatus.Failed,
                _ =>
                    OrderStatus.Pending
            };

        if (order.Status != expectedStatus)
        {
            throw new OrderPlacementPaymentOutcomeException(
                $"Placement '{process.OrderId.Value}' state '{process.State}' conflicts with order status '{order.Status}'.");
        }
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

    private static void EnsureCausationId(
        Guid causationId)
    {
        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty.",
                nameof(causationId));
        }
    }
}
