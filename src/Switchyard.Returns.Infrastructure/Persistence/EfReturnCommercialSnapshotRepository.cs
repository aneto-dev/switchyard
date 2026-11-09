using Microsoft.EntityFrameworkCore;
using Switchyard.Returns.Application.Commercial;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfReturnCommercialSnapshotRepository :
    IReturnCommercialSnapshotRepository
{
    private readonly ReturnsDbContext _dbContext;

    public EfReturnCommercialSnapshotRepository(
        ReturnsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task<ReturnCommercialSnapshot?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var record =
            await _dbContext.CommercialSnapshots
                            .AsNoTracking()
                            .Include(candidate => candidate.Lines)
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.OrderId ==
                                    orderId,
                                cancellationToken);

        return record is null
            ? null
            : new ReturnCommercialSnapshot(
                record.OrderId,
                record.PaymentId,
                record.OrderNumber,
                record.TotalAmount,
                record.Currency,
                record.ConfirmedAtUtc,
                record.Lines
                      .OrderBy(line => line.OrderLineId)
                      .Select(
                          line =>
                              new ReturnCommercialSnapshotLine(
                                  line.OrderLineId,
                                  line.Quantity,
                                  line.UnitPriceAmount,
                                  line.Currency))
                      .ToArray());
    }

    public async Task<ApplyOrderCommercialSnapshotResult> ApplyAsync(
        ReturnCommercialSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({snapshot.OrderId.ToString()}, 0));",
            cancellationToken);

        var existing =
            await _dbContext.CommercialSnapshots
                            .Include(candidate => candidate.Lines)
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.OrderId ==
                                    snapshot.OrderId,
                                cancellationToken);

        if (existing is not null)
        {
            if (!Matches(
                    existing,
                    snapshot))
            {
                throw new ReturnCommercialSnapshotConflictException(
                    snapshot.OrderId);
            }

            return new ApplyOrderCommercialSnapshotResult(
                Replayed: true);
        }

        var record =
            new ReturnCommercialSnapshotRecord
            {
                OrderId = snapshot.OrderId,
                PaymentId = snapshot.PaymentId,
                OrderNumber = snapshot.OrderNumber.Trim(),
                TotalAmount = snapshot.TotalAmount,
                Currency = snapshot.Currency.Trim().ToUpperInvariant(),
                ConfirmedAtUtc = snapshot.ConfirmedAtUtc.ToUniversalTime()
            };

        foreach (var line in snapshot.Lines
                                     .OrderBy(
                                         line =>
                                             line.OrderLineId))
        {
            record.Lines.Add(
                new ReturnCommercialSnapshotLineRecord
                {
                    OrderId = snapshot.OrderId,
                    OrderLineId = line.OrderLineId,
                    Quantity = line.Quantity,
                    UnitPriceAmount = line.UnitPriceAmount,
                    Currency = line.Currency.Trim().ToUpperInvariant()
                });
        }

        await _dbContext.CommercialSnapshots.AddAsync(
            record,
            cancellationToken);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new ApplyOrderCommercialSnapshotResult(
            Replayed: false);
    }

    private static bool Matches(
        ReturnCommercialSnapshotRecord existing,
        ReturnCommercialSnapshot snapshot)
    {
        var normalizedLines =
            snapshot.Lines
                    .OrderBy(
                        line =>
                            line.OrderLineId)
                    .ToArray();

        var existingLines =
            existing.Lines
                    .OrderBy(
                        line =>
                            line.OrderLineId)
                    .ToArray();

        return
            existing.PaymentId == snapshot.PaymentId &&
            string.Equals(
                existing.OrderNumber,
                snapshot.OrderNumber.Trim(),
                StringComparison.Ordinal) &&
            existing.TotalAmount == snapshot.TotalAmount &&
            string.Equals(
                existing.Currency,
                snapshot.Currency.Trim().ToUpperInvariant(),
                StringComparison.Ordinal) &&
            existing.ConfirmedAtUtc ==
                snapshot.ConfirmedAtUtc.ToUniversalTime() &&
            existingLines.Length ==
                normalizedLines.Length &&
            existingLines.Zip(
                normalizedLines)
                .All(
                    pair =>
                        pair.First.OrderLineId ==
                            pair.Second.OrderLineId &&
                        pair.First.Quantity ==
                            pair.Second.Quantity &&
                        pair.First.UnitPriceAmount ==
                            pair.Second.UnitPriceAmount &&
                        string.Equals(
                            pair.First.Currency,
                            pair.Second.Currency.Trim().ToUpperInvariant(),
                            StringComparison.Ordinal));
    }

    private static void Validate(
        ReturnCommercialSnapshot snapshot)
    {
        if (snapshot.OrderId == Guid.Empty ||
            snapshot.PaymentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshot.OrderNumber) ||
            snapshot.TotalAmount < 0 ||
            string.IsNullOrWhiteSpace(snapshot.Currency) ||
            snapshot.Currency.Trim().Length != 3 ||
            snapshot.ConfirmedAtUtc == default ||
            snapshot.Lines.Count == 0)
        {
            throw new ArgumentException(
                "Return commercial snapshot contains invalid required fields.",
                nameof(snapshot));
        }

        var normalizedCurrency =
            snapshot.Currency.Trim().ToUpperInvariant();

        if (snapshot.Lines.Any(
                line =>
                    line.OrderLineId == Guid.Empty ||
                    line.Quantity <= 0 ||
                    line.UnitPriceAmount < 0 ||
                    string.IsNullOrWhiteSpace(line.Currency) ||
                    !string.Equals(
                        line.Currency.Trim().ToUpperInvariant(),
                        normalizedCurrency,
                        StringComparison.Ordinal)) ||
            snapshot.Lines
                    .GroupBy(
                        line =>
                            line.OrderLineId)
                    .Any(
                        group =>
                            group.Count() > 1))
        {
            throw new ArgumentException(
                "Return commercial snapshot contains invalid line details.",
                nameof(snapshot));
        }

        var calculatedTotal =
            snapshot.Lines.Sum(
                line =>
                    line.UnitPriceAmount *
                    line.Quantity);

        if (calculatedTotal !=
            snapshot.TotalAmount)
        {
            throw new ArgumentException(
                "Return commercial snapshot line total does not match order total.",
                nameof(snapshot));
        }
    }
}

public sealed class ReturnCommercialSnapshotConflictException :
    Exception
{
    public ReturnCommercialSnapshotConflictException(
        Guid orderId)
        : base(
            $"Commercial snapshot for order '{orderId}' already exists with different details.")
    {
    }
}
