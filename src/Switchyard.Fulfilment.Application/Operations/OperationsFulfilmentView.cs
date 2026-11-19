using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Application.Operations;

public sealed record OperationsFulfilmentView(
    Guid OrderId,
    FulfilmentRequestStatus Status,
    DateTimeOffset ConfirmedAtUtc,
    Guid PaymentId,
    OperationsFulfilmentCapture Capture,
    IReadOnlyList<OperationsFulfilmentLine> Lines,
    IReadOnlyList<OperationsFulfilmentShipment> Shipments,
    IReadOnlyList<OperationsFulfilmentCancellation> Cancellations);

public sealed record OperationsFulfilmentCapture(
    FulfilmentPaymentCaptureStatus Status,
    Guid? RequestId,
    DateTimeOffset? RequestedAtUtc,
    string? ProviderReference,
    DateTimeOffset? ResolvedAtUtc);

public sealed record OperationsFulfilmentLine(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity,
    int PreparedQuantity,
    int CancelledQuantity);

public sealed record OperationsFulfilmentShipment(
    Guid ShipmentId,
    int ItemQuantity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? DeliveredAtUtc);

public sealed record OperationsFulfilmentCancellation(
    Guid CancellationId,
    FulfilmentCancellationOutcome Outcome,
    FulfilmentCancellationRejectionReason? RejectionReason,
    int Quantity,
    DateTimeOffset RequestedAtUtc);
