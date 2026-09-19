using Switchyard.Payments.Application.Authorisation;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentAuthorisationProvider
{
    Task<PaymentProviderAuthorisationResult> AuthoriseAsync(
        PaymentProviderAuthorisationRequest request, CancellationToken cancellationToken);
}
