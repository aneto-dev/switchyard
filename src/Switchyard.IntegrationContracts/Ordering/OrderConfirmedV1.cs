namespace Switchyard.IntegrationContracts.Ordering;

public sealed record OrderConfirmedV1(
    Guid OrderId,
    Guid PaymentId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<OrderConfirmedLineV1> Lines)
{
    public const string MessageType = "ordering.event.confirmed.v1";
}
