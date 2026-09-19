namespace Switchyard.Ordering.Application.Orders;

public sealed record OrderAcceptedIntegrationMessageV1(
    Guid OrderId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset AcceptedAtUtc)
{
    public const string MessageType = "ordering.order-accepted.v1";
}
