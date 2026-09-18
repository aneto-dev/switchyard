namespace Switchyard.Inventory.Application.Reservations;

public sealed class InventoryReservationPolicy
{
    public InventoryReservationPolicy(TimeSpan reservationLifetime)
    {
        if (reservationLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservationLifetime),
                "Reservation lifetime must be positive.");
        }

        ReservationLifetime = reservationLifetime;
    }

    public TimeSpan ReservationLifetime { get; }

    public DateTimeOffset CalculateExpiry(DateTimeOffset reservedAtUtc) =>
        reservedAtUtc.Add(ReservationLifetime);
}
