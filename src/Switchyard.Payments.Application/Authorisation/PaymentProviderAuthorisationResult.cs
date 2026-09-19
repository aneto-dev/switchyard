namespace Switchyard.Payments.Application.Authorisation;

public sealed record PaymentProviderAuthorisationResult(
    PaymentProviderAuthorisationOutcome Outcome,
    string ProviderReference);
