using Switchyard.Payments.Application.Refunds;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentRefundRequestStore
{
    Task<PaymentRefundRequestDecision> ApplyAsync(
        PaymentRefundRequest request,
        CancellationToken cancellationToken);
}
