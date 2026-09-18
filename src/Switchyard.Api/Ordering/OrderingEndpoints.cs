using Microsoft.AspNetCore.Http.HttpResults;
using Switchyard.Ordering.Application.Orders;

namespace Switchyard.Api.Ordering;

public static class OrderingEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/orders")
                             .WithTags("Ordering");

        group.MapPost(string.Empty, CreateOrderAsync)
             .WithName("CreateOrder");

        group.MapGet("/{orderId:guid}", GetOrderAsync)
             .WithName("GetOrder");

        return endpoints;
    }

    private static async Task<Results<
        Created<CreateOrderResponse>,
        Ok<CreateOrderResponse>,
        Conflict<CreateOrderConflictResponse>,
        ValidationProblem>> CreateOrderAsync(
        CreateOrderRequest request, CreatePendingOrderHandler handler, LinkGenerator linkGenerator,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var idempotencyKey = GetIdempotencyKey(httpContext);
        var validationErrors = CreateOrderRequestValidator.Validate(request, idempotencyKey);

        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var lines = request.Lines!.Select(line => new CreatePendingOrderLine(
                                             line!.SkuCode!,
                                             line.ProductName!,
                                             line.Quantity,
                                             line.UnitPriceAmount,
                                             line.Currency!))
                                  .ToArray();

        CreatePendingOrderResult result;

        try
        {
            result = await handler.HandleAsync(
                new CreatePendingOrderCommand(idempotencyKey!, lines),
                cancellationToken);
        }
        catch (OrderRequestConflictException exception)
        {
            return TypedResults.Conflict(new CreateOrderConflictResponse(exception.Message));
        }

        var location = linkGenerator.GetPathByName("GetOrder", new { orderId = result.OrderId });

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new InvalidOperationException("Could not generate the created order location.");
        }

        var response = new CreateOrderResponse(result.OrderId, result.OrderNumber, result.Status,
                                               result.TotalAmount, result.Currency, result.CreatedAtUtc);

        if (result.Replayed)
        {
            httpContext.Response.Headers.Location = location;
            return TypedResults.Ok(response);
        }

        return TypedResults.Created(location, response);
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrderAsync(
        Guid orderId, GetOrderHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(orderId, cancellationToken);

        if (result is null)
        {
            return TypedResults.NotFound();
        }

        var lines = result.Lines.Select(line => new OrderLineResponse(
                                         line.OrderLineId,
                                         line.SkuCode,
                                         line.ProductName,
                                         line.Quantity,
                                         line.UnitPriceAmount,
                                         line.LineTotalAmount,
                                         line.Currency))
                                .ToArray();

        var response = new OrderResponse(result.OrderId, result.OrderNumber, result.Status,
                                         result.TotalAmount, result.Currency, result.CreatedAtUtc, lines);

        return TypedResults.Ok(response);
    }

    private static string? GetIdempotencyKey(HttpContext httpContext)
    {
        var values = httpContext.Request.Headers[IdempotencyKeyHeader];

        return values.Count == 1
            ? values[0]
            : null;
    }
}
