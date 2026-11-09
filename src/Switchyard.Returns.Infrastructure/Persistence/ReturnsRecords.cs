namespace Switchyard.Returns.Infrastructure.Persistence;

internal sealed class ReturnEligibilityRecord
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public List<ReturnEligibilityLineRecord> Lines { get; } = [];
    public List<ShipmentDeliveryRecord> Deliveries { get; } = [];
    public List<ReturnRequestRecord> ReturnRequests { get; } = [];
}

internal sealed class ReturnEligibilityLineRecord
{
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public int DeliveredQuantity { get; set; }
    public int AuthorisedReturnQuantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public ReturnEligibilityRecord Eligibility { get; set; } = null!;
}

internal sealed class ShipmentDeliveryRecord
{
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public DateTimeOffset DeliveredAtUtc { get; set; }
    public ReturnEligibilityRecord Eligibility { get; set; } = null!;
    public List<ShipmentDeliveryLineRecord> Lines { get; } = [];
}

internal sealed class ShipmentDeliveryLineRecord
{
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public ShipmentDeliveryRecord Delivery { get; set; } = null!;
}

internal sealed class ReturnRequestRecord
{
    public Guid OrderId { get; set; }
    public Guid ReturnRequestId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public int Status { get; set; }
    public ReturnEligibilityRecord Eligibility { get; set; } = null!;
    public List<ReturnRequestLineRecord> Lines { get; } = [];
}

internal sealed class ReturnRequestLineRecord
{
    public Guid OrderId { get; set; }
    public Guid ReturnRequestId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public ReturnRequestRecord ReturnRequest { get; set; } = null!;
}

internal sealed class ReturnsInboxMessageRecord
{
    public string ConsumerName { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
