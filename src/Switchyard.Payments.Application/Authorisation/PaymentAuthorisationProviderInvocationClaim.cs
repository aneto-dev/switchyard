namespace Switchyard.Payments.Application.Authorisation;

public sealed record PaymentAuthorisationProviderInvocationClaim(
    PaymentAuthorisationDecision Decision,
    bool Acquired);