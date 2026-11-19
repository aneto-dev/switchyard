namespace Switchyard.Ordering.Application.Placement;

// Values are persisted. Authorised and Failed are terminal.
public enum PaymentAuthorisationOutcome
{
    Indeterminate = 0,
    NeedsAttention = 1,
    Authorised = 2,
    Failed = 3
}
