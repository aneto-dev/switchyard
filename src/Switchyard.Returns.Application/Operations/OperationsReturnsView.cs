using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Application.Operations;

public sealed record OperationsReturnsView(
    Guid OrderId,
    IReadOnlyList<OperationsReturnLine> Lines,
    IReadOnlyList<OperationsReturnRequest> Requests);

// AuthorisedReturnQuantity is authorised but not yet received; received quantity moves to
// ReturnedQuantity.
public sealed record OperationsReturnLine(
    Guid OrderLineId,
    int DeliveredQuantity,
    int AuthorisedReturnQuantity,
    int ReturnedQuantity);

public sealed record OperationsReturnRequest(
    Guid ReturnRequestId,
    ReturnRequestStatus Status,
    int Quantity,
    DateTimeOffset RequestedAtUtc,
    IReadOnlyList<OperationsReturnReceipt> Receipts);

// Refund is null when the receipt has not been valued for refund yet.
public sealed record OperationsReturnReceipt(
    Guid ReturnReceiptId,
    int Quantity,
    DateTimeOffset ReceivedAtUtc,
    OperationsReturnRefund? Refund);

public sealed record OperationsReturnRefund(
    ReturnRefundStatus Status,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset UpdatedAtUtc);
