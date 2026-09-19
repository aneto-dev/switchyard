namespace Switchyard.Payments.Application.Settlement;

public sealed class PaymentSettlementNotFoundException : Exception
{
    public PaymentSettlementNotFoundException(Guid paymentId)
        : base($"Payment '{paymentId}' does not exist.")
    {
        PaymentId = paymentId;
    }

    public Guid PaymentId { get; }
}
