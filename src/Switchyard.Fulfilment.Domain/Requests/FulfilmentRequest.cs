namespace Switchyard.Fulfilment.Domain.Requests;

public sealed class FulfilmentRequest
{
    private readonly IReadOnlyList<FulfilmentRequestLine> _lines;

    private FulfilmentRequest(
        Guid orderId,
        string orderNumber,
        IReadOnlyList<FulfilmentRequestLine> lines,
        DateTimeOffset confirmedAtUtc,
        FulfilmentRequestStatus status)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        _lines = lines;
        ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime();
        Status = status;
    }

    public Guid OrderId { get; }
    public string OrderNumber { get; }
    public IReadOnlyList<FulfilmentRequestLine> Lines => _lines;
    public DateTimeOffset ConfirmedAtUtc { get; }
    public FulfilmentRequestStatus Status { get; }

    public static FulfilmentRequest Create(
        Guid orderId,
        string orderNumber,
        IEnumerable<FulfilmentRequestLine> lines,
        DateTimeOffset confirmedAtUtc) =>
        Rehydrate(
            orderId,
            orderNumber,
            lines,
            confirmedAtUtc,
            FulfilmentRequestStatus.Pending);

    public static FulfilmentRequest Rehydrate(
        Guid orderId,
        string orderNumber,
        IEnumerable<FulfilmentRequestLine> lines,
        DateTimeOffset confirmedAtUtc,
        FulfilmentRequestStatus status)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fulfilment order ID cannot be empty.",
                nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(orderNumber) ||
            orderNumber.Trim().Length > 50)
        {
            throw new ArgumentException(
                "Fulfilment order number is required and cannot exceed 50 characters.",
                nameof(orderNumber));
        }

        if (confirmedAtUtc == default)
        {
            throw new ArgumentException(
                "Fulfilment confirmation time is required.",
                nameof(confirmedAtUtc));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unknown fulfilment request status.");
        }

        ArgumentNullException.ThrowIfNull(lines);

        var materialized = lines.ToArray();

        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A fulfilment request requires at least one line.",
                nameof(lines));
        }

        if (materialized.Any(line => line is null))
        {
            throw new ArgumentException(
                "Fulfilment request lines cannot contain null entries.",
                nameof(lines));
        }

        if (materialized
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Fulfilment order line IDs must be unique.",
                nameof(lines));
        }

        return new FulfilmentRequest(
            orderId,
            orderNumber.Trim(),
            Array.AsReadOnly(materialized),
            confirmedAtUtc,
            status);
    }
}

public sealed record FulfilmentRequestLine
{
    public FulfilmentRequestLine(
        Guid orderLineId,
        string skuCode,
        string productName,
        int quantity)
    {
        if (orderLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order line ID cannot be empty.",
                nameof(orderLineId));
        }

        if (string.IsNullOrWhiteSpace(skuCode) ||
            skuCode.Trim().Length > 100)
        {
            throw new ArgumentException(
                "SKU code is required and cannot exceed 100 characters.",
                nameof(skuCode));
        }

        if (string.IsNullOrWhiteSpace(productName) ||
            productName.Trim().Length > 200)
        {
            throw new ArgumentException(
                "Product name is required and cannot exceed 200 characters.",
                nameof(productName));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Fulfilment quantity must be positive.");
        }

        OrderLineId = orderLineId;
        SkuCode = skuCode.Trim();
        ProductName = productName.Trim();
        Quantity = quantity;
    }

    public Guid OrderLineId { get; }
    public string SkuCode { get; }
    public string ProductName { get; }
    public int Quantity { get; }
}

public enum FulfilmentRequestStatus
{
    Pending = 0
}
