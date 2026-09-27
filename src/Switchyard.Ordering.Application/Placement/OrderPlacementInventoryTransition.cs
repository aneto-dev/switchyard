namespace Switchyard.Ordering.Application.Placement;

public sealed record OrderPlacementInventoryTransition(
    bool Changed,
    bool AuthorisePayment,
    bool FailPlacement,
    IReadOnlyList<OrderPlacementRelease> Releases)
{
    public static OrderPlacementInventoryTransition None { get; } =
        new(
            Changed: false,
            AuthorisePayment: false,
            FailPlacement: false,
            Array.Empty<OrderPlacementRelease>());
}
