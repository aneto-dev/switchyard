namespace Switchyard.IntegrationContracts.Payments;

public sealed record PaymentAuthorisationIndeterminateV1(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string? ProviderReference,
    DateTimeOffset IndeterminateAtUtc)
{
    public const string MessageType = "payments.event.authorisation-indeterminate.v1";
}
