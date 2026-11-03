using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

public sealed class EfFulfilmentRequestRepository :
    IFulfilmentRequestRepository
{
    private readonly FulfilmentDbContext _dbContext;

    public EfFulfilmentRequestRepository(
        FulfilmentDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<FulfilmentRequest?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var record =
            await _dbContext.Requests
                            .AsNoTracking()
                            .Include(request => request.Lines)
                            .Include(request => request.Units)
                                .ThenInclude(unit => unit.Lines)
                            .SingleOrDefaultAsync(
                                request =>
                                    request.OrderId ==
                                    orderId,
                                cancellationToken);

        return record is null
            ? null
            : Map(record);
    }

    public async Task AddAsync(
        FulfilmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record =
            new FulfilmentRequestRecord
            {
                OrderId = request.OrderId,
                OrderNumber = request.OrderNumber,
                Status = request.Status,
                ConfirmedAtUtc = request.ConfirmedAtUtc
            };

        for (var position = 0;
             position < request.Lines.Count;
             position++)
        {
            var line =
                request.Lines[position];

            record.Lines.Add(
                new FulfilmentRequestLineRecord
                {
                    OrderId = request.OrderId,
                    OrderLineId = line.OrderLineId,
                    Position = position,
                    SkuCode = line.SkuCode,
                    ProductName = line.ProductName,
                    Quantity = line.Quantity,
                    PreparedQuantity = line.PreparedQuantity
                });
        }

        await _dbContext.Requests.AddAsync(
            record,
            cancellationToken);
    }

    public async Task<IFulfilmentRequestTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        return new EfFulfilmentRequestTransaction(
            transaction);
    }

    public async Task<FulfilmentRequest?> GetByOrderIdForUpdateAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var record =
            await _dbContext.Requests
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM fulfilment.requests
                    WHERE order_id = {orderId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (record is null)
        {
            return null;
        }

        await _dbContext.Entry(record)
            .Collection(request => request.Lines)
            .LoadAsync(
                cancellationToken);

        await _dbContext.Entry(record)
            .Collection(request => request.Units)
            .Query()
            .Include(unit => unit.Lines)
            .LoadAsync(
                cancellationToken);

        return Map(
            record);
    }

    public async Task SaveAsync(
        FulfilmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record =
            _dbContext.Requests.Local
                .SingleOrDefault(
                    candidate =>
                        candidate.OrderId ==
                        request.OrderId) ??
            throw new InvalidOperationException(
                "Fulfilment request must be loaded for update before it can be saved.");

        record.Status =
            request.Status;

        foreach (var line in request.Lines)
        {
            var persistedLine =
                record.Lines.Single(
                    candidate =>
                        candidate.OrderLineId ==
                        line.OrderLineId);

            persistedLine.PreparedQuantity =
                line.PreparedQuantity;
        }

        foreach (var unit in request.Units)
        {
            if (record.Units.Any(
                    candidate =>
                        candidate.UnitId ==
                        unit.UnitId))
            {
                continue;
            }

            var persistedUnit =
                new FulfilmentUnitRecord
                {
                    OrderId = request.OrderId,
                    UnitId = unit.UnitId,
                    CreatedAtUtc = unit.CreatedAtUtc
                };

            foreach (var unitLine in unit.Lines)
            {
                persistedUnit.Lines.Add(
                    new FulfilmentUnitLineRecord
                    {
                        OrderId = request.OrderId,
                        UnitId = unit.UnitId,
                        OrderLineId = unitLine.OrderLineId,
                        Quantity = unitLine.Quantity
                    });
            }

            record.Units.Add(
                persistedUnit);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static FulfilmentRequest Map(
        FulfilmentRequestRecord record) =>
        FulfilmentRequest.Rehydrate(
            record.OrderId,
            record.OrderNumber,
            record.Lines
                  .OrderBy(line => line.Position)
                  .Select(
                      line =>
                          new FulfilmentRequestLine(
                              line.OrderLineId,
                              line.SkuCode,
                              line.ProductName,
                              line.Quantity,
                              line.PreparedQuantity)),
            record.ConfirmedAtUtc,
            record.Status,
            record.Units
                  .OrderBy(unit => unit.CreatedAtUtc)
                  .ThenBy(unit => unit.UnitId)
                  .Select(
                      unit =>
                          new FulfilmentUnit(
                              unit.UnitId,
                              unit.Lines
                                  .OrderBy(line => line.OrderLineId)
                                  .Select(
                                      line =>
                                          new FulfilmentUnitLine(
                                              line.OrderLineId,
                                              line.Quantity)),
                              unit.CreatedAtUtc)));

    private sealed class EfFulfilmentRequestTransaction :
        IFulfilmentRequestTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfFulfilmentRequestTransaction(
            IDbContextTransaction transaction)
        {
            _transaction =
                transaction;
        }

        public ValueTask DisposeAsync() =>
            _transaction.DisposeAsync();

        public Task CommitAsync(
            CancellationToken cancellationToken) =>
            _transaction.CommitAsync(
                cancellationToken);
    }
}
