using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderPlacementProcessRepository :
    IOrderPlacementProcessRepository
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderPlacementProcessRepository(
        OrderingDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(
        OrderPlacementProcess process,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        var record = new OrderPlacementProcessRecord
        {
            OrderId = process.OrderId.Value,
            State = process.State,
            PaymentAuthorisationRequestId =
                process.PaymentAuthorisationRequestId,
            StartedAtUtc = process.StartedAtUtc,
            UpdatedAtUtc = process.UpdatedAtUtc
        };

        foreach (var line in process.Lines)
        {
            record.Lines.Add(
                new OrderPlacementLineRecord
                {
                    OrderId = process.OrderId.Value,
                    OrderLineId = line.OrderLineId.Value,
                    ReservationRequestId = line.ReservationRequestId,
                    SkuCode = line.SkuCode,
                    Quantity = line.Quantity,
                    State = line.State,
                    ReservationId = line.ReservationId,
                    ReservationExpiresAtUtc =
                        line.ReservationExpiresAtUtc
                });
        }

        await _dbContext.OrderPlacementProcesses.AddAsync(
            record,
            cancellationToken);
    }

    public async Task<OrderPlacementProcess?> GetByOrderIdAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var record =
            await _dbContext.OrderPlacementProcesses
                .AsNoTracking()
                .Include(process => process.Lines)
                .SingleOrDefaultAsync(
                    process =>
                        process.OrderId ==
                        orderId.Value,
                    cancellationToken);

        return record is null
            ? null
            : ToDomain(record);
    }

    public async Task<OrderPlacementProcess?> GetByOrderIdForUpdateAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Order-placement updates require an active Ordering transaction.");
        }

        var record =
            await _dbContext.OrderPlacementProcesses
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM ordering.order_placement_processes
                    WHERE order_id = {orderId.Value}
                    FOR UPDATE
                    """)
                .AsTracking()
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (record is null)
        {
            return null;
        }

        await _dbContext.Entry(record)
            .Collection(process => process.Lines)
            .LoadAsync(cancellationToken);

        return ToDomain(record);
    }

    public Task UpdateAsync(
        OrderPlacementProcess process,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        cancellationToken.ThrowIfCancellationRequested();

        var record =
            _dbContext.OrderPlacementProcesses.Local
                .SingleOrDefault(
                    candidate =>
                        candidate.OrderId ==
                        process.OrderId.Value);

        if (record is null)
        {
            throw new InvalidOperationException(
                "Order-placement process must be loaded for update before it can be persisted.");
        }

        record.State = process.State;
        record.UpdatedAtUtc = process.UpdatedAtUtc;
        record.PaymentAuthorisationRequestId =
            process.PaymentAuthorisationRequestId;

        foreach (var line in process.Lines)
        {
            var lineRecord =
                record.Lines.SingleOrDefault(
                    candidate =>
                        candidate.OrderLineId ==
                        line.OrderLineId.Value);

            if (lineRecord is null)
            {
                throw new InvalidOperationException(
                    $"Order-placement line '{line.OrderLineId.Value}' is missing from persistence.");
            }

            lineRecord.State = line.State;
            lineRecord.ReservationId = line.ReservationId;
            lineRecord.ReservationExpiresAtUtc =
                line.ReservationExpiresAtUtc;
        }

        return Task.CompletedTask;
    }

    private static OrderPlacementProcess ToDomain(
        OrderPlacementProcessRecord record)
    {
        var lines =
            record.Lines
                .OrderBy(line => line.OrderLineId)
                .Select(
                    line =>
                        OrderPlacementLine.Rehydrate(
                            new OrderLineId(
                                line.OrderLineId),
                            line.ReservationRequestId,
                            line.SkuCode,
                            line.Quantity,
                            line.State,
                            line.ReservationId,
                            line.ReservationExpiresAtUtc))
                .ToArray();

        return OrderPlacementProcess.Rehydrate(
            new OrderId(record.OrderId),
            record.State,
            record.PaymentAuthorisationRequestId,
            record.StartedAtUtc,
            record.UpdatedAtUtc,
            lines);
    }
}
