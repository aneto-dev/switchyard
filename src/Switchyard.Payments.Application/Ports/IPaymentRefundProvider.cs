using Switchyard.Payments.Application.Refunds;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentRefundProvider
{
    Task<PaymentProviderRefundResult> RefundAsync(
        PaymentProviderRefundRequest request,
        CancellationToken cancellationToken);
}
