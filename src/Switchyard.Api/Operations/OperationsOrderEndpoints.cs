using Microsoft.AspNetCore.Http.HttpResults;
using Switchyard.Fulfilment.Application.Operations;
using Switchyard.Ordering.Application.Operations;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Api.Operations;

public static class OperationsOrderEndpoints
{
    private const int MaxOrderNumberLength = 32;

    public static IEndpointRouteBuilder MapOperationsOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/operations/orders")
                             .WithTags("Operations");

        group.MapGet(string.Empty, ListOrdersAsync)
             .WithName("ListOperationsOrders");

        group.MapGet("/{orderId:guid}", GetOrderAsync)
             .WithName("GetOperationsOrder");

        group.MapGet("/{orderId:guid}/fulfilment", GetFulfilmentAsync)
             .WithName("GetOperationsOrderFulfilment");

        return endpoints;
    }

    private static async Task<Results<Ok<OperationsOrderSummaryResponse[]>, ValidationProblem>> ListOrdersAsync(
        string? status, string? orderNumber, int? limit, IOperationsOrderQueries queries,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        OrderStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Match names only: Enum.TryParse would also accept numbers and comma-separated flags.
            var name = Enum.GetNames<OrderStatus>()
                           .FirstOrDefault(candidate => string.Equals(
                               candidate, status.Trim(), StringComparison.OrdinalIgnoreCase));

            if (name is not null)
            {
                statusFilter = Enum.Parse<OrderStatus>(name);
            }
            else
            {
                errors["status"] = [$"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}."];
            }
        }

        if (orderNumber is not null && orderNumber.Trim().Length > MaxOrderNumberLength)
        {
            errors["orderNumber"] = [$"Order number cannot exceed {MaxOrderNumberLength} characters."];
        }

        var resolvedLimit = limit ?? OperationsOrderListFilter.DefaultLimit;

        if (resolvedLimit is < 1 or > OperationsOrderListFilter.MaxLimit)
        {
            errors["limit"] = [$"Limit must be between 1 and {OperationsOrderListFilter.MaxLimit}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var orders = await queries.ListRecentAsync(
            new OperationsOrderListFilter(statusFilter, orderNumber, resolvedLimit),
            cancellationToken);

        var response = orders.Select(order => new OperationsOrderSummaryResponse(
                                 order.OrderId,
                                 order.OrderNumber,
                                 order.Status.ToString(),
                                 order.PlacementState?.ToString(),
                                 order.TotalAmount,
                                 order.Currency,
                                 order.LineCount,
                                 order.CreatedAtUtc,
                                 order.PlacementUpdatedAtUtc))
                             .ToArray();

        return TypedResults.Ok(response);
    }

    // Read from Fulfilment's own tables. 404 means Fulfilment has not accepted the order yet,
    // which is normal until the order is confirmed.
    private static async Task<Results<Ok<OperationsFulfilmentResponse>, NotFound>> GetFulfilmentAsync(
        Guid orderId, IOperationsFulfilmentQueries queries, CancellationToken cancellationToken)
    {
        var fulfilment = await queries.GetByOrderIdAsync(orderId, cancellationToken);

        if (fulfilment is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new OperationsFulfilmentResponse(
            fulfilment.OrderId,
            fulfilment.Status.ToString(),
            fulfilment.ConfirmedAtUtc,
            fulfilment.PaymentId,
            new OperationsFulfilmentCaptureResponse(
                fulfilment.Capture.Status.ToString(),
                fulfilment.Capture.RequestId,
                fulfilment.Capture.RequestedAtUtc,
                fulfilment.Capture.ProviderReference,
                fulfilment.Capture.ResolvedAtUtc),
            fulfilment.Lines.Select(line => new OperationsFulfilmentLineResponse(
                                line.OrderLineId,
                                line.SkuCode,
                                line.ProductName,
                                line.Quantity,
                                line.PreparedQuantity,
                                line.CancelledQuantity))
                            .ToArray(),
            fulfilment.Shipments.Select(shipment => new OperationsFulfilmentShipmentResponse(
                                    shipment.ShipmentId,
                                    shipment.ItemQuantity,
                                    shipment.CreatedAtUtc,
                                    shipment.DispatchedAtUtc,
                                    shipment.DeliveredAtUtc))
                                .ToArray(),
            fulfilment.Cancellations.Select(cancellation => new OperationsFulfilmentCancellationResponse(
                                        cancellation.CancellationId,
                                        cancellation.Outcome.ToString(),
                                        cancellation.RejectionReason?.ToString(),
                                        cancellation.Quantity,
                                        cancellation.RequestedAtUtc))
                                    .ToArray()));
    }

    private static async Task<Results<Ok<OperationsOrderDetailResponse>, NotFound>> GetOrderAsync(
        Guid orderId, IOperationsOrderQueries queries, CancellationToken cancellationToken)
    {
        var order = await queries.GetAsync(orderId, cancellationToken);

        if (order is null)
        {
            return TypedResults.NotFound();
        }

        var placement = order.Placement is null
            ? null
            : new OperationsOrderPlacementResponse(
                order.Placement.State.ToString(),
                order.Placement.StartedAtUtc,
                order.Placement.UpdatedAtUtc,
                order.Placement.PaymentAuthorisationRequestId);

        var payment = order.Payment is null
            ? null
            : new OperationsOrderPaymentResponse(
                order.Payment.Outcome.ToString(),
                order.Payment.RequestId,
                order.Payment.PaymentId,
                order.Payment.Amount,
                order.Payment.Currency,
                order.Payment.ProviderReference,
                order.Payment.Reason,
                order.Payment.ReconciliationAttemptCount,
                order.Payment.OutcomeAtUtc,
                order.Payment.RecordedAtUtc);

        var lines = order.Lines.Select(line => new OperationsOrderLineResponse(
                                    line.OrderLineId,
                                    line.SkuCode,
                                    line.ProductName,
                                    line.Quantity,
                                    line.UnitPriceAmount,
                                    line.LineTotalAmount,
                                    line.Currency,
                                    line.ReservationState?.ToString(),
                                    line.ReservationRequestId,
                                    line.ReservationId,
                                    line.ReservationExpiresAtUtc))
                         .ToArray();

        return TypedResults.Ok(new OperationsOrderDetailResponse(
            order.OrderId,
            order.OrderNumber,
            order.Status.ToString(),
            order.TotalAmount,
            order.Currency,
            order.CreatedAtUtc,
            placement,
            payment,
            lines));
    }
}
