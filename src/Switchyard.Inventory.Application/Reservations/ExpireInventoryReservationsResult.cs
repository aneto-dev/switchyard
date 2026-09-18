namespace Switchyard.Inventory.Application.Reservations;

public sealed record ExpireInventoryReservationsResult(int ExpiredCount, DateTimeOffset ExpiredAtUtc);
