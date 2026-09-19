namespace Switchyard.Payments.Application.Authorisation;

public sealed class PaymentProviderIndeterminateException : Exception
{
    public PaymentProviderIndeterminateException(string? providerReference = null)
        : base("The payment provider may have processed the authorisation but its outcome is not safely known.")
    {
        ProviderReference = providerReference;
    }

    public string? ProviderReference { get; }
}
