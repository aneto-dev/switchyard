using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class EfOrderPaymentAuthorisationRepository : IOrderPaymentAuthorisationRepository
{
    private readonly OrderingDbContext _dbContext;

    public EfOrderPaymentAuthorisationRepository(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OrderPaymentAuthorisation?> GetByOrderIdAsync(
        OrderId orderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        var record = await _dbContext.OrderPaymentAuthorisations
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId.Value, cancellationToken);

        return record is null
            ? null
            : OrderPaymentAuthorisation.Rehydrate(
                new OrderId(record.OrderId),
                record.RequestId,
                record.PaymentId,
                record.Outcome,
                record.Amount,
                record.Currency,
                record.ProviderReference,
                record.Reason,
                record.ReconciliationAttemptCount,
                record.OutcomeAtUtc,
                record.RecordedAtUtc);
    }

    public async Task AddAsync(OrderPaymentAuthorisation authorisation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorisation);

        var record = new OrderPaymentAuthorisationRecord
        {
            OrderId = authorisation.OrderId.Value,
            RequestId = authorisation.RequestId
        };

        CopyOutcome(authorisation, record);

        await _dbContext.OrderPaymentAuthorisations.AddAsync(record, cancellationToken);
    }

    public Task UpdateAsync(OrderPaymentAuthorisation authorisation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorisation);
        cancellationToken.ThrowIfCancellationRequested();

        var record = _dbContext.OrderPaymentAuthorisations.Local
            .SingleOrDefault(candidate => candidate.OrderId == authorisation.OrderId.Value)
            ?? throw new InvalidOperationException(
                "Payment authorisation must be loaded before it can be updated.");

        CopyOutcome(authorisation, record);

        return Task.CompletedTask;
    }

    private static void CopyOutcome(OrderPaymentAuthorisation source, OrderPaymentAuthorisationRecord target)
    {
        target.PaymentId = source.PaymentId;
        target.Outcome = source.Outcome;
        target.Amount = source.Amount;
        target.Currency = source.Currency;
        target.ProviderReference = source.ProviderReference;
        target.Reason = source.Reason;
        target.ReconciliationAttemptCount = source.ReconciliationAttemptCount;
        target.OutcomeAtUtc = source.OutcomeAtUtc;
        target.RecordedAtUtc = source.RecordedAtUtc;
    }
}
