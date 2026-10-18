namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentAuthorisedV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string ProviderReference,
    DateTimeOffset AuthorisedAtUtc)
{
    public const string MessageType = "payments.event.authorised.v1";
}