namespace Switchyard.Ordering.Application.Placement;

public sealed class OrderPlacementInventoryOutcomeException :
    InvalidOperationException
{
    public OrderPlacementInventoryOutcomeException(string message)
        : base(message)
    {
    }
}
