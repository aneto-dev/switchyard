namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderPlacementPaymentOutcomeException :
    InvalidOperationException
{
    public OrderPlacementPaymentOutcomeException(string message)
        : base(message)
    {
    }
}
