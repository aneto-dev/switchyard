using Microsoft.EntityFrameworkCore;
using Switchyard.Inventory.Application.Ports;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Infrastructure.Persistence;

public sealed class EfInventoryReservationLifecycleStore :
    IInventoryReservationLifecycleStore
{
    private readonly InventoryDbContext _dbContext;

    public EfInventoryReservationLifecycleStore(
        InventoryDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<InventoryReservationReleaseDecision> ReleaseAsync(
        Guid requestId,
        Guid orderId,
        Guid reservationId,
        StockReservationReleaseReason reason,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken)
    {
        var ownsTransaction =
            _dbContext.Database.CurrentTransaction is null;

        await using var transaction =
            ownsTransaction
                ? await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken)
                : null;

        var reservation =
            await _dbContext.ReservationRequests
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM inventory.reservation_requests
                    WHERE reservation_id = {reservationId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (reservation is null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(
                    cancellationToken);
            }

            return new InventoryReservationReleaseDecision(
                requestId,
                orderId,
                reservationId,
                ReleaseInventoryOutcome.NotFound,
                ReleaseReason: null,
                ReleasedAtUtc: null,
                ExpiredAtUtc: null);
        }

        if (reservation.RequestId != requestId ||
            reservation.OrderId != orderId)
        {
            throw new InventoryReservationReleaseConflictException(
                reservationId);
        }

        if (reservation.ReleasedAtUtc is not null)
        {
            var appliedReason =
                reservation.ReleaseReason ??
                throw new InvalidOperationException(
                    "Released reservation is missing its release reason.");

            if (transaction is not null)
            {
                await transaction.CommitAsync(
                    cancellationToken);
            }

            return new InventoryReservationReleaseDecision(
                reservation.RequestId,
                reservation.OrderId,
                reservationId,
                ReleaseInventoryOutcome.AlreadyReleased,
                appliedReason,
                reservation.ReleasedAtUtc,
                ExpiredAtUtc: null);
        }

        if (reservation.ExpiredAtUtc is not null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(
                    cancellationToken);
            }

            return new InventoryReservationReleaseDecision(
                reservation.RequestId,
                reservation.OrderId,
                reservationId,
                ReleaseInventoryOutcome.AlreadyExpired,
                ReleaseReason: null,
                ReleasedAtUtc: null,
                reservation.ExpiredAtUtc);
        }

        await ReturnStockAsync(
            reservation,
            cancellationToken);

        reservation.ReleasedAtUtc =
            releasedAtUtc;

        reservation.ReleaseReason =
            reason;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(
                cancellationToken);
        }

        return new InventoryReservationReleaseDecision(
            reservation.RequestId,
            reservation.OrderId,
            reservationId,
            ReleaseInventoryOutcome.Released,
            reason,
            reservation.ReleasedAtUtc,
            ExpiredAtUtc: null);
    }

    public async Task<int> ExpireAsync(
        DateTimeOffset expiredAtUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var reservations =
            await _dbContext.ReservationRequests
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
                .ToListAsync(
                    cancellationToken);

        foreach (var reservation in reservations)
        {
            await ReturnStockAsync(
                reservation,
                cancellationToken);

            reservation.ExpiredAtUtc =
                expiredAtUtc;
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return reservations.Count;
    }

    private async Task ReturnStockAsync(
        ReservationRequestRecord reservation,
        CancellationToken cancellationToken)
    {
        var affectedRows =
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
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
