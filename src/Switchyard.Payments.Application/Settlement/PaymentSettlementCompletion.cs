using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed record PaymentSettlementCompletion(
    Guid RequestId,
    PaymentSettlementStatus Status,
    string? ProviderReference,
    DateTimeOffset ResolvedAtUtc);
