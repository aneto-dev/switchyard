namespace Switchyard.Api.Operations;

public sealed record OperationsFulfilmentResponse(
    Guid OrderId,
    string Status,
    DateTimeOffset ConfirmedAtUtc,
    Guid PaymentId,
    OperationsFulfilmentCaptureResponse Capture,
    IReadOnlyList<OperationsFulfilmentLineResponse> Lines,
    IReadOnlyList<OperationsFulfilmentShipmentResponse> Shipments,
    IReadOnlyList<OperationsFulfilmentCancellationResponse> Cancellations);

public sealed record OperationsFulfilmentCaptureResponse(
    string Status,
    Guid? RequestId,
    DateTimeOffset? RequestedAtUtc,
    string? ProviderReference,
    DateTimeOffset? ResolvedAtUtc);

public sealed record OperationsFulfilmentLineResponse(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity,
    int PreparedQuantity,
    int CancelledQuantity);

public sealed record OperationsFulfilmentShipmentResponse(
    Guid ShipmentId,
    int ItemQuantity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? DeliveredAtUtc);

public sealed record OperationsFulfilmentCancellationResponse(
    Guid CancellationId,
    string Outcome,
    string? RejectionReason,
    int Quantity,
    DateTimeOffset RequestedAtUtc);
