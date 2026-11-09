using Switchyard.Payments.Application.Ports;

namespace Switchyard.Payments.Application.Refunds;

public sealed class ApplyPaymentRefundRequestHandler
{
    private readonly IPaymentRefundRequestStore _store;

    public ApplyPaymentRefundRequestHandler(
        IPaymentRefundRequestStore store)
    {
        _store =
            store ??
            throw new ArgumentNullException(
                nameof(store));
    }

    public Task<PaymentRefundRequestDecision> HandleAsync(
        ApplyPaymentRefundRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _store.ApplyAsync(
            new PaymentRefundRequest(
                command.RequestId,
                command.PaymentId,
                command.OrderId,
                command.ReturnRequestId,
                command.ReturnReceiptId,
                command.Amount,
                command.Currency,
                command.RequestedAtUtc),
            cancellationToken);
    }
}

public sealed record ApplyPaymentRefundRequestCommand(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    DateTimeOffset RequestedAtUtc);

public sealed record PaymentRefundRequest(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    DateTimeOffset RequestedAtUtc);

public sealed record PaymentRefundRequestDecision(
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    bool Replayed);
