using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed record ExecutePaymentSettlementCommand(
    Guid RequestId,
    Guid PaymentId,
    PaymentSettlementAction Action);
