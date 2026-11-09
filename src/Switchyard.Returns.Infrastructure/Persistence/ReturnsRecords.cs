namespace Switchyard.Returns.Infrastructure.Persistence;

internal sealed class ReturnRefundRecord
{
    public Guid OrderId { get; set; }
    public Guid ReturnReceiptId { get; set; }
    public Guid ReturnRequestId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? ProviderReference { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

namespace Switchyard.Returns.Infrastructure.Persistence;

internal sealed class ReturnCommercialSnapshotRecord
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAtUtc { get; set; }
    public List<ReturnCommercialSnapshotLineRecord> Lines { get; } = [];
}

internal sealed class ReturnCommercialSnapshotLineRecord
{
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public ReturnCommercialSnapshotRecord Snapshot { get; set; } = null!;
}

internal sealed class ReturnEligibilityRecord
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public List<ReturnEligibilityLineRecord> Lines { get; } = [];
    public List<ShipmentDeliveryRecord> Deliveries { get; } = [];
    public List<ReturnRequestRecord> ReturnRequests { get; } = [];
    public List<ReturnReceiptRecord> ReturnReceipts { get; } = [];
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

internal sealed class ReturnReceiptRecord
{
    public Guid OrderId { get; set; }
    public Guid ReturnReceiptId { get; set; }
    public Guid ReturnRequestId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public ReturnEligibilityRecord Eligibility { get; set; } = null!;
    public ReturnRequestRecord ReturnRequest { get; set; } = null!;
    public List<ReturnReceiptLineRecord> Lines { get; } = [];
}

internal sealed class ReturnReceiptLineRecord
{
    public Guid OrderId { get; set; }
    public Guid ReturnReceiptId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public ReturnReceiptRecord ReturnReceipt { get; set; } = null!;
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

internal sealed class ReturnsOutboxMessageRecord
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public int DeliveryAttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public Guid? LockToken { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? LastError { get; set; }
}
