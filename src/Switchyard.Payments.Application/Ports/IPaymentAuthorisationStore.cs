using Switchyard.Payments.Application.Authorisation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentAuthorisationStore
{
    Task<PaymentAuthorisationDecision> BeginAsync(
        PaymentAuthorisationRequest request, CancellationToken cancellationToken);

    Task<PaymentAuthorisationDecision> CompleteAsync(
        PaymentAuthorisationCompletion completion, CancellationToken cancellationToken);
}
