namespace Switchyard.IntegrationContracts.Fulfilment;

public sealed record ShipmentLineV1(
    Guid OrderLineId,
    int Quantity);
