using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

internal sealed class FulfilmentRequestRecord
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public FulfilmentRequestStatus Status { get; set; }
    public DateTimeOffset ConfirmedAtUtc { get; set; }
    public Guid? PaymentCaptureRequestId { get; set; }
    public DateTimeOffset? PaymentCaptureRequestedAtUtc { get; set; }
    public FulfilmentPaymentCaptureStatus PaymentCaptureStatus { get; set; }
    public string? PaymentCaptureProviderReference { get; set; }
    public DateTimeOffset? PaymentCaptureResolvedAtUtc { get; set; }
    public List<FulfilmentRequestLineRecord> Lines { get; } = [];
    public List<FulfilmentUnitRecord> Units { get; } = [];
    public List<FulfilmentCancellationRecord> Cancellations { get; } = [];
    public List<FulfilmentShipmentRecord> Shipments { get; } = [];
}

internal sealed class FulfilmentRequestLineRecord
{
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Position { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int PreparedQuantity { get; set; }
    public int CancelledQuantity { get; set; }
    public FulfilmentRequestRecord Request { get; set; } = null!;
}

internal sealed class FulfilmentUnitRecord
{
    public Guid OrderId { get; set; }
    public Guid UnitId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public FulfilmentRequestRecord Request { get; set; } = null!;
    public List<FulfilmentUnitLineRecord> Lines { get; } = [];
}

internal sealed class FulfilmentUnitLineRecord
{
    public Guid OrderId { get; set; }
    public Guid UnitId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public FulfilmentUnitRecord Unit { get; set; } = null!;
}

internal sealed class FulfilmentCancellationRecord
{
    public Guid OrderId { get; set; }
    public Guid CancellationId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public FulfilmentCancellationOutcome Outcome { get; set; }
    public FulfilmentCancellationRejectionReason? RejectionReason { get; set; }
    public FulfilmentRequestRecord Request { get; set; } = null!;
    public List<FulfilmentCancellationLineRecord> Lines { get; } = [];
}

internal sealed class FulfilmentCancellationLineRecord
{
    public Guid OrderId { get; set; }
    public Guid CancellationId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public FulfilmentCancellationRecord Cancellation { get; set; } = null!;
}

internal sealed class FulfilmentInboxMessageRecord
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

internal sealed class FulfilmentShipmentRecord
{
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public FulfilmentRequestRecord Request { get; set; } = null!;
    public List<FulfilmentShipmentUnitRecord> Units { get; } = [];
}

internal sealed class FulfilmentShipmentUnitRecord
{
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid UnitId { get; set; }
    public FulfilmentShipmentRecord Shipment { get; set; } = null!;
}

internal sealed class FulfilmentOutboxMessageRecord
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
