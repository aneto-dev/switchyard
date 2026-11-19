namespace Switchyard.Api.Operations;

public sealed record OperationsReturnsResponse(
    Guid OrderId,
    IReadOnlyList<OperationsReturnLineResponse> Lines,
    IReadOnlyList<OperationsReturnRequestResponse> Requests);

public sealed record OperationsReturnLineResponse(
    Guid OrderLineId,
    int DeliveredQuantity,
    int AuthorisedReturnQuantity,
    int ReturnedQuantity);

public sealed record OperationsReturnRequestResponse(
    Guid ReturnRequestId,
    string Status,
    int Quantity,
    DateTimeOffset RequestedAtUtc,
    IReadOnlyList<OperationsReturnReceiptResponse> Receipts);

public sealed record OperationsReturnReceiptResponse(
    Guid ReturnReceiptId,
    int Quantity,
    DateTimeOffset ReceivedAtUtc,
    OperationsReturnRefundResponse? Refund);

public sealed record OperationsReturnRefundResponse(
    string Status,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset UpdatedAtUtc);
