namespace Switchyard.IntegrationContracts.Fulfilment;

public sealed record ShipmentDeliveredV1(
    Guid OrderId,
    Guid ShipmentId,
    string OrderNumber,
    IReadOnlyList<ShipmentLineV1> Lines,
    DateTimeOffset DeliveredAtUtc)
{
    public const string MessageType =
        "fulfilment.event.shipment-delivered.v1";
}
