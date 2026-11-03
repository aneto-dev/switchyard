namespace Switchyard.Fulfilment.Domain.Requests;

public sealed class FulfilmentRequest
{
    private readonly IReadOnlyList<FulfilmentRequestLine> _lines;
    private readonly List<FulfilmentUnit> _units;
    private readonly List<FulfilmentCancellation> _cancellations;

    private FulfilmentRequest(
        Guid orderId,
        string orderNumber,
        IReadOnlyList<FulfilmentRequestLine> lines,
        IEnumerable<FulfilmentUnit> units,
        IEnumerable<FulfilmentCancellation> cancellations,
        DateTimeOffset confirmedAtUtc,
        FulfilmentRequestStatus status)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        _lines = lines;
        _units = units.ToList();
        _cancellations = cancellations.ToList();
        ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime();
        Status = status;
    }

    public Guid OrderId { get; }

    public string OrderNumber { get; }

    public IReadOnlyList<FulfilmentRequestLine> Lines => _lines;

    public IReadOnlyList<FulfilmentUnit> Units => _units;

    public IReadOnlyList<FulfilmentCancellation> Cancellations => _cancellations;

    public DateTimeOffset ConfirmedAtUtc { get; }

    public FulfilmentRequestStatus Status { get; private set; }

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
            FulfilmentRequestStatus.Pending,
            [],
            []);

    public static FulfilmentRequest Rehydrate(
        Guid orderId,
        string orderNumber,
        IEnumerable<FulfilmentRequestLine> lines,
        DateTimeOffset confirmedAtUtc,
        FulfilmentRequestStatus status,
        IEnumerable<FulfilmentUnit>? units = null,
        IEnumerable<FulfilmentCancellation>? cancellations = null)
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

        var materializedLines =
            lines.ToArray();

        if (materializedLines.Length == 0)
        {
            throw new ArgumentException(
                "A fulfilment request requires at least one line.",
                nameof(lines));
        }

        if (materializedLines.Any(line => line is null))
        {
            throw new ArgumentException(
                "Fulfilment request lines cannot contain null entries.",
                nameof(lines));
        }

        if (materializedLines
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Fulfilment order line IDs must be unique.",
                nameof(lines));
        }

        var materializedUnits =
            (units ?? []).ToArray();

        if (materializedUnits
            .GroupBy(unit => unit.UnitId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Fulfilment unit IDs must be unique.",
                nameof(units));
        }

        var materializedCancellations =
            (cancellations ?? []).ToArray();

        if (materializedCancellations
            .GroupBy(cancellation => cancellation.CancellationId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Fulfilment cancellation IDs must be unique.",
                nameof(cancellations));
        }

        var lineMap =
            materializedLines.ToDictionary(
                line => line.OrderLineId);

        foreach (var unit in materializedUnits)
        {
            foreach (var unitLine in unit.Lines)
            {
                if (!lineMap.ContainsKey(
                        unitLine.OrderLineId))
                {
                    throw new ArgumentException(
                        "Fulfilment units can only reference lines owned by the request.",
                        nameof(units));
                }
            }
        }

        foreach (var cancellation in materializedCancellations)
        {
            foreach (var cancellationLine in cancellation.Lines)
            {
                if (!lineMap.ContainsKey(
                        cancellationLine.OrderLineId))
                {
                    throw new ArgumentException(
                        "Fulfilment cancellations can only reference lines owned by the request.",
                        nameof(cancellations));
                }
            }
        }

        foreach (var line in materializedLines)
        {
            var preparedByUnits =
                materializedUnits
                    .SelectMany(unit => unit.Lines)
                    .Where(
                        unitLine =>
                            unitLine.OrderLineId ==
                            line.OrderLineId)
                    .Sum(unitLine => unitLine.Quantity);

            if (preparedByUnits !=
                line.PreparedQuantity)
            {
                throw new ArgumentException(
                    "Prepared line quantity must match the quantities assigned to fulfilment units.",
                    nameof(units));
            }

            var cancelledByAcceptedRequests =
                materializedCancellations
                    .Where(
                        cancellation =>
                            cancellation.Outcome ==
                            FulfilmentCancellationOutcome.Accepted)
                    .SelectMany(cancellation => cancellation.Lines)
                    .Where(
                        cancellationLine =>
                            cancellationLine.OrderLineId ==
                            line.OrderLineId)
                    .Sum(cancellationLine => cancellationLine.Quantity);

            if (cancelledByAcceptedRequests !=
                line.CancelledQuantity)
            {
                throw new ArgumentException(
                    "Cancelled line quantity must match accepted fulfilment cancellations.",
                    nameof(cancellations));
            }
        }

        var expectedStatus =
            CalculateStatus(
                materializedLines);

        if (status != expectedStatus)
        {
            throw new ArgumentException(
                "Fulfilment request status does not match prepared quantities.",
                nameof(status));
        }

        return new FulfilmentRequest(
            orderId,
            orderNumber.Trim(),
            Array.AsReadOnly(
                materializedLines),
            materializedUnits,
            materializedCancellations,
            confirmedAtUtc,
            status);
    }

    public bool CreateUnit(
        Guid unitId,
        IEnumerable<FulfilmentUnitLine> lines,
        DateTimeOffset createdAtUtc)
    {
        if (unitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fulfilment unit ID cannot be empty.",
                nameof(unitId));
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentException(
                "Fulfilment unit creation time is required.",
                nameof(createdAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var materialized =
            lines.ToArray();

        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A fulfilment unit requires at least one line.",
                nameof(lines));
        }

        if (materialized
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A fulfilment unit cannot contain the same order line more than once.",
                nameof(lines));
        }

        var existing =
            _units.SingleOrDefault(
                unit =>
                    unit.UnitId ==
                    unitId);

        var requested =
            new FulfilmentUnit(
                unitId,
                materialized,
                createdAtUtc);

        if (existing is not null)
        {
            if (existing.Matches(
                    requested))
            {
                return false;
            }

            throw new FulfilmentUnitConflictException(
                unitId);
        }

        foreach (var unitLine in materialized)
        {
            var requestLine =
                _lines.SingleOrDefault(
                    line =>
                        line.OrderLineId ==
                        unitLine.OrderLineId) ??
                throw new ArgumentException(
                    $"Order line '{unitLine.OrderLineId}' does not belong to this fulfilment request.",
                    nameof(lines));

            if (unitLine.Quantity >
                requestLine.CancellableQuantity)
            {
                throw new InvalidOperationException(
                    $"Cannot prepare {unitLine.Quantity} units for order line '{unitLine.OrderLineId}' because only {requestLine.CancellableQuantity} remain.");
            }
        }

        foreach (var unitLine in materialized)
        {
            _lines.Single(
                    line =>
                        line.OrderLineId ==
                        unitLine.OrderLineId)
                .Prepare(
                    unitLine.Quantity);
        }

        _units.Add(
            requested);

        Status =
            CalculateStatus(
                _lines);

        return true;
    }

    public FulfilmentCancellationDecision ApplyCancellation(
        Guid cancellationId,
        IEnumerable<FulfilmentCancellationLine> lines,
        DateTimeOffset requestedAtUtc)
    {
        if (cancellationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fulfilment cancellation ID cannot be empty.",
                nameof(cancellationId));
        }

        if (requestedAtUtc == default)
        {
            throw new ArgumentException(
                "Fulfilment cancellation request time is required.",
                nameof(requestedAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var materialized =
            lines.ToArray();

        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A fulfilment cancellation requires at least one line.",
                nameof(lines));
        }

        if (materialized
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A fulfilment cancellation cannot contain the same order line more than once.",
                nameof(lines));
        }

        var existing =
            _cancellations.SingleOrDefault(
                cancellation =>
                    cancellation.CancellationId ==
                    cancellationId);

        if (existing is not null)
        {
            var replay =
                new FulfilmentCancellation(
                    cancellationId,
                    materialized,
                    requestedAtUtc,
                    existing.Outcome,
                    existing.RejectionReason);

            if (!existing.MatchesRequest(
                    replay))
            {
                throw new FulfilmentCancellationConflictException(
                    cancellationId);
            }

            return new FulfilmentCancellationDecision(
                Replayed: true,
                existing.Outcome,
                existing.RejectionReason);
        }

        foreach (var cancellationLine in materialized)
        {
            _ =
                _lines.SingleOrDefault(
                    line =>
                        line.OrderLineId ==
                        cancellationLine.OrderLineId) ??
                throw new ArgumentException(
                    $"Order line '{cancellationLine.OrderLineId}' does not belong to this fulfilment request.",
                    nameof(lines));
        }

        var unavailable =
            materialized.Any(
                cancellationLine =>
                {
                    var requestLine =
                        _lines.Single(
                            line =>
                                line.OrderLineId ==
                                cancellationLine.OrderLineId);

                    return cancellationLine.Quantity >
                        requestLine.CancellableQuantity;
                });

        if (unavailable)
        {
            _cancellations.Add(
                new FulfilmentCancellation(
                    cancellationId,
                    materialized,
                    requestedAtUtc,
                    FulfilmentCancellationOutcome.Rejected,
                    FulfilmentCancellationRejectionReason.QuantityNoLongerCancellable));

            return new FulfilmentCancellationDecision(
                Replayed: false,
                FulfilmentCancellationOutcome.Rejected,
                FulfilmentCancellationRejectionReason.QuantityNoLongerCancellable);
        }

        foreach (var cancellationLine in materialized)
        {
            _lines.Single(
                    line =>
                        line.OrderLineId ==
                        cancellationLine.OrderLineId)
                .Cancel(
                    cancellationLine.Quantity);
        }

        _cancellations.Add(
            new FulfilmentCancellation(
                cancellationId,
                materialized,
                requestedAtUtc,
                FulfilmentCancellationOutcome.Accepted,
                null));

        return new FulfilmentCancellationDecision(
            Replayed: false,
            FulfilmentCancellationOutcome.Accepted,
            null);
    }

    private static FulfilmentRequestStatus CalculateStatus(
        IEnumerable<FulfilmentRequestLine> lines)
    {
        var materialized =
            lines.ToArray();

        if (materialized.All(
                line =>
                    line.PreparedQuantity ==
                    0))
        {
            return FulfilmentRequestStatus.Pending;
        }

        if (materialized.All(
                line =>
                    line.PreparedQuantity ==
                    line.Quantity))
        {
            return FulfilmentRequestStatus.Prepared;
        }

        return FulfilmentRequestStatus.PartiallyPrepared;
    }
}

public sealed class FulfilmentRequestLine
{
    public FulfilmentRequestLine(
        Guid orderLineId,
        string skuCode,
        string productName,
        int quantity,
        int preparedQuantity = 0,
        int cancelledQuantity = 0)
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

        if (preparedQuantity < 0 ||
            cancelledQuantity < 0 ||
            preparedQuantity +
                cancelledQuantity >
                quantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preparedQuantity),
                "Prepared and cancelled quantities must fit within the ordered quantity.");
        }

        OrderLineId = orderLineId;
        SkuCode = skuCode.Trim();
        ProductName = productName.Trim();
        Quantity = quantity;
        PreparedQuantity = preparedQuantity;
        CancelledQuantity = cancelledQuantity;
    }

    public Guid OrderLineId { get; }

    public string SkuCode { get; }

    public string ProductName { get; }

    public int Quantity { get; }

    public int PreparedQuantity { get; private set; }

    public int CancelledQuantity { get; private set; }

    public int CancellableQuantity =>
        Quantity -
        PreparedQuantity -
        CancelledQuantity;

    internal void Prepare(
        int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Prepared quantity must be positive.");
        }

        if (quantity >
            CancellableQuantity)
        {
            throw new InvalidOperationException(
                "Prepared quantity exceeds the remaining cancellable quantity.");
        }

        PreparedQuantity += quantity;
    }

    internal void Cancel(
        int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Cancelled quantity must be positive.");
        }

        if (quantity >
            CancellableQuantity)
        {
            throw new InvalidOperationException(
                "Cancelled quantity exceeds the remaining cancellable quantity.");
        }

        CancelledQuantity += quantity;
    }
}

public sealed class FulfilmentUnit
{
    private readonly IReadOnlyList<FulfilmentUnitLine> _lines;

    public FulfilmentUnit(
        Guid unitId,
        IEnumerable<FulfilmentUnitLine> lines,
        DateTimeOffset createdAtUtc)
    {
        if (unitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fulfilment unit ID cannot be empty.",
                nameof(unitId));
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentException(
                "Fulfilment unit creation time is required.",
                nameof(createdAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var materialized =
            lines.ToArray();

        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A fulfilment unit requires at least one line.",
                nameof(lines));
        }

        if (materialized
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Fulfilment unit line IDs must be unique.",
                nameof(lines));
        }

        UnitId = unitId;
        _lines = Array.AsReadOnly(
            materialized);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid UnitId { get; }

    public IReadOnlyList<FulfilmentUnitLine> Lines => _lines;

    public DateTimeOffset CreatedAtUtc { get; }

    internal bool Matches(
        FulfilmentUnit other) =>
        CreatedAtUtc ==
            other.CreatedAtUtc &&
        Lines.SequenceEqual(
            other.Lines);
}

public sealed record FulfilmentUnitLine
{
    public FulfilmentUnitLine(
        Guid orderLineId,
        int quantity)
    {
        if (orderLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order line ID cannot be empty.",
                nameof(orderLineId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Fulfilment unit quantity must be positive.");
        }

        OrderLineId = orderLineId;
        Quantity = quantity;
    }

    public Guid OrderLineId { get; }

    public int Quantity { get; }
}

public sealed class FulfilmentCancellation
{
    private readonly IReadOnlyList<FulfilmentCancellationLine> _lines;

    public FulfilmentCancellation(
        Guid cancellationId,
        IEnumerable<FulfilmentCancellationLine> lines,
        DateTimeOffset requestedAtUtc,
        FulfilmentCancellationOutcome outcome,
        FulfilmentCancellationRejectionReason? rejectionReason)
    {
        if (cancellationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fulfilment cancellation ID cannot be empty.",
                nameof(cancellationId));
        }

        if (requestedAtUtc == default)
        {
            throw new ArgumentException(
                "Fulfilment cancellation request time is required.",
                nameof(requestedAtUtc));
        }

        ArgumentNullException.ThrowIfNull(lines);

        var materialized =
            lines.ToArray();

        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A fulfilment cancellation requires at least one line.",
                nameof(lines));
        }

        if (materialized
            .GroupBy(line => line.OrderLineId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Fulfilment cancellation line IDs must be unique.",
                nameof(lines));
        }

        if (outcome == FulfilmentCancellationOutcome.Accepted &&
            rejectionReason is not null ||
            outcome == FulfilmentCancellationOutcome.Rejected &&
            rejectionReason is null)
        {
            throw new ArgumentException(
                "Fulfilment cancellation rejection reason must match the outcome.",
                nameof(rejectionReason));
        }

        CancellationId = cancellationId;
        _lines = Array.AsReadOnly(materialized);
        RequestedAtUtc = requestedAtUtc.ToUniversalTime();
        Outcome = outcome;
        RejectionReason = rejectionReason;
    }

    public Guid CancellationId { get; }

    public IReadOnlyList<FulfilmentCancellationLine> Lines => _lines;

    public DateTimeOffset RequestedAtUtc { get; }

    public FulfilmentCancellationOutcome Outcome { get; }

    public FulfilmentCancellationRejectionReason? RejectionReason { get; }

    internal bool MatchesRequest(
        FulfilmentCancellation other) =>
        RequestedAtUtc ==
            other.RequestedAtUtc &&
        Lines.SequenceEqual(
            other.Lines);
}

public sealed record FulfilmentCancellationLine
{
    public FulfilmentCancellationLine(
        Guid orderLineId,
        int quantity)
    {
        if (orderLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "Order line ID cannot be empty.",
                nameof(orderLineId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Fulfilment cancellation quantity must be positive.");
        }

        OrderLineId = orderLineId;
        Quantity = quantity;
    }

    public Guid OrderLineId { get; }

    public int Quantity { get; }
}

public sealed record FulfilmentCancellationDecision(
    bool Replayed,
    FulfilmentCancellationOutcome Outcome,
    FulfilmentCancellationRejectionReason? RejectionReason);

public enum FulfilmentCancellationOutcome
{
    Accepted = 0,
    Rejected = 1
}

public enum FulfilmentCancellationRejectionReason
{
    QuantityNoLongerCancellable = 0
}

public enum FulfilmentRequestStatus
{
    Pending = 0,
    PartiallyPrepared = 1,
    Prepared = 2
}

public sealed class FulfilmentUnitConflictException : Exception
{
    public FulfilmentUnitConflictException(
        Guid unitId)
        : base(
            $"Fulfilment unit '{unitId}' already exists with different details.")
    {
    }
}

public sealed class FulfilmentCancellationConflictException : Exception
{
    public FulfilmentCancellationConflictException(
        Guid cancellationId)
        : base(
            $"Fulfilment cancellation '{cancellationId}' already exists with different request details.")
    {
    }
}
