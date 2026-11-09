using Microsoft.EntityFrameworkCore;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.RefundExecution;
using Switchyard.Payments.Application.RefundReconciliation;
using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class EfPaymentRefundLifecycleStore :
    IPaymentRefundExecutionStore,
    IPaymentRefundReconciliationStore
{
    private readonly PaymentsDbContext _dbContext;

    public EfPaymentRefundLifecycleStore(
        PaymentsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task<IReadOnlyList<Guid>> GetPendingRequestIdsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);

        return await _dbContext.RefundRequests
                               .AsNoTracking()
                               .Where(
                                   record =>
                                       record.Status ==
                                       PaymentRefundStatus.Pending)
                               .OrderBy(
                                   record =>
                                       record.RequestedAtUtc)
                               .ThenBy(
                                   record =>
                                       record.RequestId)
                               .Select(
                                   record =>
                                       record.RequestId)
                               .Take(batchSize)
                               .ToArrayAsync(
                                   cancellationToken);
    }

    public async Task<PaymentRefundProviderInvocationClaim> ClaimProviderInvocationAsync(
        Guid requestId,
        DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Refund request ID cannot be empty.",
                nameof(requestId));
        }

        var normalizedClaimedAtUtc =
            claimedAtUtc.ToUniversalTime();

        _dbContext.ChangeTracker.Clear();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var record =
            await _dbContext.RefundRequests
                            .FromSqlInterpolated(
                                $"""
                                SELECT *
                                FROM payments.refund_requests
                                WHERE request_id = {requestId}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(
                                cancellationToken) ??
            throw new InvalidOperationException(
                $"Refund request '{requestId}' does not exist.");

        if (record.Status !=
            PaymentRefundStatus.Pending)
        {
            var terminal =
                ToState(record);

            await transaction.CommitAsync(
                cancellationToken);

            _dbContext.ChangeTracker.Clear();

            return new PaymentRefundProviderInvocationClaim(
                terminal,
                Acquired: false);
        }

        if (record.ProviderInvocationClaimedAtUtc.HasValue)
        {
            var claimed =
                ToState(record);

            await transaction.CommitAsync(
                cancellationToken);

            _dbContext.ChangeTracker.Clear();

            return new PaymentRefundProviderInvocationClaim(
                claimed,
                Acquired: false);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            normalizedClaimedAtUtc,
            record.RequestedAtUtc);

        record.ProviderInvocationClaimedAtUtc =
            normalizedClaimedAtUtc;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        var state =
            ToState(record);

        await transaction.CommitAsync(
            cancellationToken);

        _dbContext.ChangeTracker.Clear();

        return new PaymentRefundProviderInvocationClaim(
            state,
            Acquired: true);
    }

    public async Task<PaymentRefundExecutionDecision> CompleteAsync(
        PaymentRefundExecutionCompletion completion,
        Func<PaymentRefundExecutionState, CancellationToken, Task>? onApplied,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            completion);

        if (completion.Status is not
            (PaymentRefundStatus.Succeeded or
             PaymentRefundStatus.Indeterminate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completion),
                "Refund execution completion must be succeeded or indeterminate.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var record =
            await _dbContext.RefundRequests
                            .FromSqlInterpolated(
                                $"""
                                SELECT *
                                FROM payments.refund_requests
                                WHERE request_id = {completion.RequestId}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(
                                cancellationToken) ??
            throw new InvalidOperationException(
                $"Refund request '{completion.RequestId}' does not exist.");

        if (record.Status !=
            PaymentRefundStatus.Pending)
        {
            var existing =
                ToState(record);

            await transaction.CommitAsync(
                cancellationToken);

            return new PaymentRefundExecutionDecision(
                existing,
                Applied: false);
        }

        if (!record.ProviderInvocationClaimedAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "Refund provider completion requires a durable invocation claim.");
        }

        if (completion.Status ==
                PaymentRefundStatus.Succeeded &&
            string.IsNullOrWhiteSpace(
                completion.ProviderReference))
        {
            throw new ArgumentException(
                "Successful refund completion requires a provider reference.",
                nameof(completion));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            completion.ResolvedAtUtc.ToUniversalTime(),
            record.ProviderInvocationClaimedAtUtc.Value);

        record.Status =
            completion.Status;

        record.ProviderReference =
            string.IsNullOrWhiteSpace(
                completion.ProviderReference)
                ? null
                : completion.ProviderReference.Trim();

        record.ResolvedAtUtc =
            completion.ResolvedAtUtc.ToUniversalTime();

        var state =
            ToState(record);

        if (onApplied is not null)
        {
            await onApplied(
                state,
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return new PaymentRefundExecutionDecision(
            state,
            Applied: true);
    }

    public async Task<IReadOnlyList<Guid>> GetDueRequestIdsAsync(
        int batchSize,
        DateTimeOffset nowUtc,
        TimeSpan retryDelay,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);

        ArgumentOutOfRangeException.ThrowIfLessThan(
            retryDelay,
            TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxAttempts);

        var dueBefore =
            nowUtc.ToUniversalTime()
                  .Subtract(retryDelay);

        return await _dbContext.RefundRequests
                               .AsNoTracking()
                               .Where(
                                   record =>
                                       record.Status ==
                                           PaymentRefundStatus.Indeterminate &&
                                       record.ReconciliationAttemptCount <
                                           maxAttempts &&
                                       (record.LastReconciledAtUtc == null ||
                                        record.LastReconciledAtUtc <=
                                            dueBefore))
                               .OrderBy(
                                   record =>
                                       record.LastReconciledAtUtc ??
                                       record.ResolvedAtUtc)
                               .ThenBy(
                                   record =>
                                       record.RequestId)
                               .Select(
                                   record =>
                                       record.RequestId)
                               .Take(batchSize)
                               .ToArrayAsync(
                                   cancellationToken);
    }

    public async Task<PaymentRefundExecutionState?> GetAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Refund request ID cannot be empty.",
                nameof(requestId));
        }

        var record =
            await _dbContext.RefundRequests
                            .AsNoTracking()
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.RequestId ==
                                    requestId,
                                cancellationToken);

        return record is null
            ? null
            : ToState(record);
    }

    public async Task<PaymentRefundReconciliationDecision> ApplyAtomicallyAsync(
        PaymentRefundReconciliation reconciliation,
        Func<PaymentRefundExecutionState, CancellationToken, Task>? onApplied,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            reconciliation);

        if (reconciliation.Status is not
            (PaymentRefundStatus.Succeeded or
             PaymentRefundStatus.NotApplied or
             PaymentRefundStatus.Indeterminate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliation),
                "Refund reconciliation must resolve to succeeded, not-applied or indeterminate.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var record =
            await _dbContext.RefundRequests
                            .FromSqlInterpolated(
                                $"""
                                SELECT *
                                FROM payments.refund_requests
                                WHERE request_id = {reconciliation.RequestId}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(
                                cancellationToken) ??
            throw new InvalidOperationException(
                $"Refund request '{reconciliation.RequestId}' does not exist.");

        if (record.Status !=
                PaymentRefundStatus.Indeterminate ||
            record.ReconciliationAttemptCount !=
                reconciliation.ExpectedReconciliationAttemptCount)
        {
            var unchanged =
                ToState(record);

            await transaction.CommitAsync(
                cancellationToken);

            return new PaymentRefundReconciliationDecision(
                unchanged,
                Applied: false);
        }

        var reconciledAtUtc =
            reconciliation.ReconciledAtUtc.ToUniversalTime();

        if (record.ResolvedAtUtc.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                reconciledAtUtc,
                record.ResolvedAtUtc.Value);
        }

        record.ReconciliationAttemptCount++;
        record.LastReconciledAtUtc =
            reconciledAtUtc;

        record.Status =
            reconciliation.Status;

        if (!string.IsNullOrWhiteSpace(
                reconciliation.ProviderReference))
        {
            record.ProviderReference =
                reconciliation.ProviderReference.Trim();
        }

        if (reconciliation.Status is
            PaymentRefundStatus.Succeeded or
            PaymentRefundStatus.NotApplied)
        {
            record.ResolvedAtUtc =
                reconciledAtUtc;
        }

        if (record.Status ==
                PaymentRefundStatus.Succeeded &&
            string.IsNullOrWhiteSpace(
                record.ProviderReference))
        {
            throw new InvalidOperationException(
                "Successful refund reconciliation requires a provider reference.");
        }

        var state =
            ToState(record);

        if (onApplied is not null)
        {
            await onApplied(
                state,
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return new PaymentRefundReconciliationDecision(
            state,
            Applied: true);
    }

    private static PaymentRefundExecutionState ToState(
        PaymentRefundRequestRecord record) =>
        new(
            record.RequestId,
            record.PaymentId,
            record.OrderId,
            record.ReturnRequestId,
            record.ReturnReceiptId,
            record.Amount,
            record.Currency,
            record.Status,
            record.ProviderIdempotencyKey,
            record.ProviderReference,
            record.RequestedAtUtc,
            record.ProviderInvocationClaimedAtUtc,
            record.ResolvedAtUtc,
            record.ReconciliationAttemptCount,
            record.LastReconciledAtUtc);
}
