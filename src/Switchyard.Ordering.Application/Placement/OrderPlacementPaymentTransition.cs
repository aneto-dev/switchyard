namespace Switchyard.Ordering.Application.Placement;

public sealed record OrderPlacementPaymentTransition(
    bool Changed,
    bool ConfirmOrder,
    IReadOnlyList<OrderPlacementRelease> Releases)
{
    public static OrderPlacementPaymentTransition None { get; } =
        new(
            Changed: false,
            ConfirmOrder: false,
            Array.Empty<OrderPlacementRelease>());
}
