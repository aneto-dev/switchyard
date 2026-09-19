namespace Switchyard.Payments.Application.Reconciliation;

public sealed class PaymentAuthorisationNotFoundException : Exception
{
    public PaymentAuthorisationNotFoundException(Guid requestId)
        : base($"Payment authorisation request '{requestId}' does not exist.")
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}
