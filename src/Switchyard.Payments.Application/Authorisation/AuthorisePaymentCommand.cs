namespace Switchyard.Payments.Application.Authorisation;

public sealed record AuthorisePaymentCommand(
    Guid RequestId,
    Guid OrderId,
    decimal Amount,
    string Currency);
