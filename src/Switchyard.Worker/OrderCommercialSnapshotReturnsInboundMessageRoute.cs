using System.Text.Json;
using Switchyard.IntegrationContracts.Ordering;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Commercial;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class OrderCommercialSnapshotReturnsInboundMessageRoute :
    IReturnsInboundMessageRoute
{
    public string MessageType =>
        OrderCommercialSnapshotV1.MessageType;

    public async Task HandleAsync(
        ReturnsDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        OrderCommercialSnapshotV1 snapshot;

        try
        {
            snapshot =
                JsonSerializer.Deserialize<OrderCommercialSnapshotV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Order commercial snapshot payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Order commercial snapshot payload is invalid: {exception.Message}");
        }

        Validate(
            message,
            snapshot);

        var handler =
            new ApplyOrderCommercialSnapshotHandler(
                new EfReturnCommercialSnapshotRepository(
                    dbContext));

        try
        {
            await handler.HandleAsync(
                new ApplyOrderCommercialSnapshotCommand(
                    snapshot.OrderId,
                    snapshot.PaymentId,
                    snapshot.OrderNumber,
                    snapshot.TotalAmount,
                    snapshot.Currency,
                    snapshot.ConfirmedAtUtc,
                    snapshot.Lines.Select(
                        line =>
                            new ApplyOrderCommercialSnapshotLine(
                                line.OrderLineId,
                                line.Quantity,
                                line.UnitPriceAmount,
                                line.Currency))
                                  .ToArray()),
                cancellationToken);
        }
        catch (ReturnCommercialSnapshotConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
    }

    private static void Validate(
        IntegrationMessageEnvelope message,
        OrderCommercialSnapshotV1 snapshot)
    {
        if (snapshot.OrderId == Guid.Empty ||
            snapshot.PaymentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshot.OrderNumber) ||
            snapshot.TotalAmount < 0 ||
            string.IsNullOrWhiteSpace(snapshot.Currency) ||
            snapshot.Currency.Trim().Length != 3 ||
            snapshot.ConfirmedAtUtc == default ||
            snapshot.Lines is null ||
            snapshot.Lines.Count == 0)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order commercial snapshot contains invalid required fields.");
        }

        if (message.CorrelationId !=
            snapshot.OrderId)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order commercial snapshot correlation ID must match the order ID.");
        }

        if (message.OccurredAtUtc !=
            snapshot.ConfirmedAtUtc)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order commercial snapshot occurrence time must match the confirmation time.");
        }

        var currency =
            snapshot.Currency.Trim().ToUpperInvariant();

        if (snapshot.Lines.Any(
                line =>
                    line.OrderLineId == Guid.Empty ||
                    line.Quantity <= 0 ||
                    line.UnitPriceAmount < 0 ||
                    string.IsNullOrWhiteSpace(line.Currency) ||
                    !string.Equals(
                        line.Currency.Trim().ToUpperInvariant(),
                        currency,
                        StringComparison.Ordinal)) ||
            snapshot.Lines
                    .GroupBy(
                        line =>
                            line.OrderLineId)
                    .Any(
                        group =>
                            group.Count() > 1))
        {
            throw new NonRetryableIntegrationMessageException(
                "Order commercial snapshot contains invalid line details.");
        }

        var calculatedTotal =
            snapshot.Lines.Sum(
                line =>
                    line.UnitPriceAmount *
                    line.Quantity);

        if (calculatedTotal !=
            snapshot.TotalAmount)
        {
            throw new NonRetryableIntegrationMessageException(
                "Order commercial snapshot line total does not match order total.");
        }
    }
}
