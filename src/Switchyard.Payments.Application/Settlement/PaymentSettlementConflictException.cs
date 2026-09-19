using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed class PaymentSettlementConflictException : Exception
{
    public PaymentSettlementConflictException(
        Guid requestId, Guid paymentId, PaymentSettlementAction action)
        : base("The payment settlement identity conflicts with existing settlement data.")
    {
        RequestId = requestId;
        PaymentId = paymentId;
        Action = action;
    }

    public Guid RequestId { get; }

    public Guid PaymentId { get; }

    public PaymentSettlementAction Action { get; }
}
