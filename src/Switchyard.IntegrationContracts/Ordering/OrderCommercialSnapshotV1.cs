namespace Switchyard.IntegrationContracts.Ordering;

public sealed record OrderCommercialSnapshotV1(
    Guid OrderId,
    Guid PaymentId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<OrderCommercialSnapshotLineV1> Lines)
{
    public const string MessageType =
        "ordering.event.commercial-snapshot.v1";
}
