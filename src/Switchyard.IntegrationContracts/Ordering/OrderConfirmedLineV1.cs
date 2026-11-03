namespace Switchyard.IntegrationContracts.Ordering;

public sealed record OrderConfirmedLineV1(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity);
