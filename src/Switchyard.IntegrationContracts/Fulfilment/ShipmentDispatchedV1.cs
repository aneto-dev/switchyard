namespace Switchyard.IntegrationContracts.Fulfilment;

public sealed record ShipmentDispatchedV1(
    Guid OrderId,
    Guid ShipmentId,
    string OrderNumber,
    IReadOnlyList<ShipmentLineV1> Lines,
    DateTimeOffset DispatchedAtUtc)
{
    public const string MessageType =
        "fulfilment.event.shipment-dispatched.v1";
}
