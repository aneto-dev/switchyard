using Switchyard.Payments.Application.Settlement;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentSettlementProvider
{
    Task<PaymentProviderSettlementResult> ExecuteAsync(
        PaymentProviderSettlementRequest request, CancellationToken cancellationToken);
}
