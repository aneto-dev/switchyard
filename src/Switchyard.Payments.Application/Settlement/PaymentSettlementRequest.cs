using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed record PaymentSettlementRequest(
    Guid RequestId,
    Guid PaymentId,
    PaymentSettlementAction Action,
    DateTimeOffset RequestedAtUtc);
