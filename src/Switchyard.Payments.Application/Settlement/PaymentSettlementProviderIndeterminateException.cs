namespace Switchyard.Payments.Application.Settlement;

public sealed class PaymentSettlementProviderIndeterminateException : Exception
{
    public PaymentSettlementProviderIndeterminateException(string? providerReference = null)
        : base("The payment provider may have processed the settlement but its outcome is not safely known.")
    {
        ProviderReference = providerReference;
    }

    public string? ProviderReference { get; }
}
