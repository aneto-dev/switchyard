using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Orders;

public sealed record AcceptedOrderRequest(
    string IdempotencyKey,
    string RequestFingerprint,
    OrderId OrderId,
    DateTimeOffset AcceptedAtUtc);
