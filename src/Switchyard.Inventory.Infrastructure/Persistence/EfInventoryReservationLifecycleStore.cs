using Microsoft.EntityFrameworkCore;
using Switchyard.Inventory.Application.Ports;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Infrastructure.Persistence;

public sealed class EfInventoryReservationLifecycleStore : IInventoryReservationLifecycleStore
{
    private readonly InventoryDbContext _dbContext;

    public EfInventoryReservationLifecycleStore(InventoryDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ReleaseInventoryOutcome> ReleaseAsync(
        Guid reservationId, StockReservationReleaseReason reason,
        DateTimeOffset releasedAtUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var reservation = await _dbContext.ReservationRequests
                                          .FromSqlInterpolated(
                                              $"""
                                              SELECT *
                                              FROM inventory.reservation_requests
                                              WHERE reservation_id = {reservationId}
                                              FOR UPDATE
                                              """)
                                          .SingleOrDefaultAsync(cancellationToken);

        if (reservation is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReleaseInventoryOutcome.NotFound;
        }

        if (reservation.ReleasedAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReleaseInventoryOutcome.AlreadyReleased;
        }

        if (reservation.ExpiredAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return ReleaseInventoryOutcome.AlreadyExpired;
        }

        await ReturnStockAsync(reservation, cancellationToken);
        reservation.ReleasedAtUtc = releasedAtUtc;
        reservation.ReleaseReason = reason;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReleaseInventoryOutcome.Released;
    }

    public async Task<int> ExpireAsync(
        DateTimeOffset expiredAtUtc, int batchSize,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var reservations = await _dbContext.ReservationRequests
                                           .FromSqlInterpolated(
                                               $"""
                                               SELECT *
                                               FROM inventory.reservation_requests
                                               WHERE outcome = 0
                                                 AND released_at_utc IS NULL
                                                 AND expired_at_utc IS NULL
                                                 AND expires_at_utc <= {expiredAtUtc}
                                               ORDER BY expires_at_utc, request_id
                                               FOR UPDATE SKIP LOCKED
                                               LIMIT {batchSize}
                                               """)
                                           .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            await ReturnStockAsync(reservation, cancellationToken);
            reservation.ExpiredAtUtc = expiredAtUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return reservations.Count;
    }

    private async Task ReturnStockAsync(
        ReservationRequestRecord reservation, CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE inventory.stock_items
            SET reserved_quantity = reserved_quantity - {reservation.Quantity}
            WHERE sku_code = {reservation.SkuCode}
              AND reserved_quantity >= {reservation.Quantity};
            """,
            cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Unable to return reserved stock for reservation '{reservation.ReservationId}'.");
        }
    }
}
