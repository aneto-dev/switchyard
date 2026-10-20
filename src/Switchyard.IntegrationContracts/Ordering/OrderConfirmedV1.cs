namespace Switchyard.IntegrationContracts.Ordering;

public sealed record OrderConfirmedV1(
    Guid OrderId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ConfirmedAtUtc)
{
    public const string MessageType = "ordering.event.confirmed.v1";
}
