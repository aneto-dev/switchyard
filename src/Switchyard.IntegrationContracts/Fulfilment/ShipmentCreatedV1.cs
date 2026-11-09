namespace Switchyard.IntegrationContracts.Fulfilment;

public sealed record ShipmentCreatedV1(
    Guid OrderId,
    Guid ShipmentId,
    string OrderNumber,
    IReadOnlyList<ShipmentLineV1> Lines,
    DateTimeOffset CreatedAtUtc)
{
    public const string MessageType =
        "fulfilment.event.shipment-created.v1";
}
