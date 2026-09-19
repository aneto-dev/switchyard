namespace Switchyard.Payments.Application.Authorisation;

public sealed class PaymentAuthorisationConflictException : Exception
{
    public PaymentAuthorisationConflictException(Guid requestId, Guid orderId)
        : base("The payment authorisation identity conflicts with existing payment data.")
    {
        RequestId = requestId;
        OrderId = orderId;
    }

    public Guid RequestId { get; }

    public Guid OrderId { get; }
}
