namespace Switchyard.Payments.Application.Refunds;

public sealed class PaymentRefundConflictException : Exception
{
    public PaymentRefundConflictException(
        Guid requestId)
        : base(
            $"Payment refund request '{requestId}' conflicts with previously persisted logical work.")
    {
    }
}

public sealed class PaymentRefundNotAllowedException : Exception
{
    public PaymentRefundNotAllowedException(
        Guid paymentId,
        string reason)
        : base(
            $"Payment '{paymentId}' cannot accept the refund request: {reason}")
    {
    }
}
