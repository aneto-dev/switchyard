namespace Switchyard.Ordering.Application.Placement;

public sealed record OrderPlacementPaymentTransition(
    bool Changed,
    bool ConfirmOrder,
    bool FailOrder,
    IReadOnlyList<OrderPlacementRelease> Releases)
{
    public static OrderPlacementPaymentTransition None { get; } =
        new(
            Changed: false,
            ConfirmOrder: false,
            FailOrder: false,
            Array.Empty<OrderPlacementRelease>());
}
