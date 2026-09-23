namespace Switchyard.Ordering.Application.Placement;

public enum OrderPlacementState
{
    Started = 0,
    AwaitingInventory = 1,
    AwaitingPayment = 2,
    PaymentIndeterminate = 3,
    Confirming = 4,
    Confirmed = 5,
    CompensatingInventory = 6,
    Failed = 7,
    NeedsAttention = 8
}
