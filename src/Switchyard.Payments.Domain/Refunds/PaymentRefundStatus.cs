namespace Switchyard.Payments.Domain.Refunds;

public enum PaymentRefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Indeterminate = 2,
    NotApplied = 3
}
