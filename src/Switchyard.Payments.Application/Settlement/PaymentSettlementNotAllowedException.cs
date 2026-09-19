using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Application.Settlement;

public sealed class PaymentSettlementNotAllowedException : Exception
{
    public PaymentSettlementNotAllowedException(
        Guid paymentId, PaymentAuthorisationStatus authorisationStatus)
        : base($"Payment '{paymentId}' cannot be settled from authorisation status '{authorisationStatus}'.")
    {
        PaymentId = paymentId;
        AuthorisationStatus = authorisationStatus;
    }

    public Guid PaymentId { get; }

    public PaymentAuthorisationStatus AuthorisationStatus { get; }
}
