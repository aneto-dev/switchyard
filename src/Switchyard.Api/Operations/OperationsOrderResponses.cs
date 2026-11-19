namespace Switchyard.Api.Operations;

public sealed record OperationsOrderSummaryResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    string? PlacementState,
    decimal TotalAmount,
    string Currency,
    int LineCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PlacementUpdatedAtUtc);

public sealed record OperationsOrderDetailResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    OperationsOrderPlacementResponse? Placement,
    OperationsOrderPaymentResponse? Payment,
    IReadOnlyList<OperationsOrderLineResponse> Lines);

public sealed record OperationsOrderPlacementResponse(
    string State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? PaymentAuthorisationRequestId);

public sealed record OperationsOrderPaymentResponse(
    string Outcome,
    Guid RequestId,
    Guid? PaymentId,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    string? Reason,
    int? ReconciliationAttemptCount,
    DateTimeOffset OutcomeAtUtc,
    DateTimeOffset RecordedAtUtc);

public sealed record OperationsOrderLineResponse(
    Guid OrderLineId,
    string SkuCode,
    string ProductName,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount,
    string Currency,
    string? ReservationState,
    Guid? ReservationRequestId,
    Guid? ReservationId,
    DateTimeOffset? ReservationExpiresAtUtc);
