namespace Switchyard.IntegrationContracts.Ordering;

public sealed record OrderCommercialSnapshotLineV1(
    Guid OrderLineId,
    int Quantity,
    decimal UnitPriceAmount,
    string Currency);
