namespace Switchyard.IntegrationContracts.Payments;

public sealed record AuthorisePaymentV1(
    Guid RequestId,
    Guid OrderId,
    decimal Amount,
    string Currency)
{
    public const string MessageType = "payments.command.authorise.v1";
}
