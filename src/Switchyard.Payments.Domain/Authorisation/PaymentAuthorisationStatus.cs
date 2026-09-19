namespace Switchyard.Payments.Domain.Authorisation;

public enum PaymentAuthorisationStatus
{
    AuthorisationPending = 0,
    Authorised = 1,
    Declined = 2,
    Indeterminate = 3
}
