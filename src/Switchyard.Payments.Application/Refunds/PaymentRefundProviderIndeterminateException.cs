namespace Switchyard.Payments.Application.Refunds;

public sealed class PaymentRefundProviderIndeterminateException : Exception
{
    public PaymentRefundProviderIndeterminateException(
        string? providerReference = null)
        : base(
            "The payment provider may have processed the refund but its outcome is not safely known.")
    {
        ProviderReference = providerReference;
    }

    public string? ProviderReference { get; }
}
