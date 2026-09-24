using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Inventory.Application.Ports;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Switchyard.Inventory.Domain.Stock;

namespace Switchyard.Inventory.Infrastructure.Persistence;

public sealed class EfInventoryReservationStore : IInventoryReservationStore
{
    private const string ReservationRequestPrimaryKey = "pk_inventory_reservation_requests";

    private readonly InventoryDbContext _dbContext;

    public EfInventoryReservationStore(InventoryDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<InventoryReservationDecision> ReserveAsync(
        InventoryReservationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await GetExistingAsync(request.RequestId, cancellationToken);

        if (existing is not null)
        {
            return ResolveExisting(existing, request);
        }

        var ownsTransaction =
            _dbContext.Database.CurrentTransaction is null;

        await using var transaction =
            ownsTransaction
                ? await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken)
                : null;

        try
        {
            var affectedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE inventory.stock_items
                SET reserved_quantity = reserved_quantity + {request.Quantity}
                WHERE sku_code = {request.Sku.Value}
                  AND on_hand_quantity - reserved_quantity >= {request.Quantity};
                """,
                cancellationToken);

            var record = affectedRows == 1
                ? CreateReservedRecord(request)
                : CreateRejectedRecord(request);

            await _dbContext.ReservationRequests.AddAsync(record, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ToDecision(record, replayed: false);
        }
        catch (DbUpdateException exception)
            when (transaction is not null &&
                  IsDuplicateRequest(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var concurrent = await GetExistingAsync(request.RequestId, cancellationToken);

            if (concurrent is null)
            {
                throw new InvalidOperationException(
                    "The concurrent inventory reservation request could not be loaded after the conflict.");
            }

            return ResolveExisting(concurrent, request);
        }
    }

    private async Task<ReservationRequestRecord?> GetExistingAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        return await _dbContext.ReservationRequests.AsNoTracking()
                                                   .SingleOrDefaultAsync(
                                                       record => record.RequestId == requestId,
                                                       cancellationToken);
    }

    private static ReservationRequestRecord CreateReservedRecord(InventoryReservationRequest request)
    {
        return new ReservationRequestRecord
        {
            RequestId = request.RequestId,
            OrderId = request.OrderId,
            SkuCode = request.Sku.Value,
            Quantity = request.Quantity,
            Outcome = InventoryReservationOutcome.Reserved,
            ReservationId = StockReservationId.New().Value,
            RequestedAtUtc = request.RequestedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc
        };
    }

    private static ReservationRequestRecord CreateRejectedRecord(InventoryReservationRequest request)
    {
        return new ReservationRequestRecord
        {
            RequestId = request.RequestId,
            OrderId = request.OrderId,
            SkuCode = request.Sku.Value,
            Quantity = request.Quantity,
            Outcome = InventoryReservationOutcome.InsufficientStock,
            ReservationId = null,
            RequestedAtUtc = request.RequestedAtUtc,
            ExpiresAtUtc = null
        };
    }

    private static InventoryReservationDecision ResolveExisting(
        ReservationRequestRecord existing, InventoryReservationRequest request)
    {
        if (existing.OrderId != request.OrderId ||
            !string.Equals(existing.SkuCode, request.Sku.Value, StringComparison.Ordinal) ||
            existing.Quantity != request.Quantity)
        {
            throw new InventoryReservationConflictException(request.RequestId);
        }

        return ToDecision(existing, replayed: true);
    }

    private static InventoryReservationDecision ToDecision(
        ReservationRequestRecord record, bool replayed)
    {
        if (record.Outcome == InventoryReservationOutcome.InsufficientStock)
        {
            return new InventoryReservationDecision(
                InventoryReservationOutcome.InsufficientStock,
                null,
                replayed);
        }

        if (record.ReservationId is null || record.ExpiresAtUtc is null)
        {
            throw new InvalidOperationException(
                "A reserved inventory request is missing its reservation identity or expiry.");
        }

        var reservation = new StockReservation(
            new StockReservationId(record.ReservationId.Value),
            record.RequestId,
            record.OrderId,
            new InventorySku(record.SkuCode),
            record.Quantity,
            record.RequestedAtUtc,
            record.ExpiresAtUtc.Value);

        return new InventoryReservationDecision(
            InventoryReservationOutcome.Reserved,
            reservation,
            replayed);
    }

    private static bool IsDuplicateRequest(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ReservationRequestPrimaryKey
        };
    }
}
