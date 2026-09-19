using Switchyard.Payments.Application.Settlement;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentSettlementStore
{
    Task<PaymentSettlementDecision> BeginAsync(
        PaymentSettlementRequest request, CancellationToken cancellationToken);

    Task<PaymentSettlementDecision> CompleteAsync(
        PaymentSettlementCompletion completion, CancellationToken cancellationToken);
}
