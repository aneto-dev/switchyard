namespace Switchyard.Payments.Application.SettlementReconciliation;

public sealed class PaymentSettlementReconciliationNotFoundException : Exception
{
    public PaymentSettlementReconciliationNotFoundException(Guid requestId)
        : base($"Payment settlement request '{requestId}' does not exist.")
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}
