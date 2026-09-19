using Microsoft.EntityFrameworkCore;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Reconciliation;
using Switchyard.Payments.Domain.Authorisation;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class EfPaymentAuthorisationReconciliationStore :
    IPaymentAuthorisationReconciliationStore
{
    private readonly PaymentsDbContext _dbContext;

    public EfPaymentAuthorisationReconciliationStore(PaymentsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PaymentAuthorisationReconciliationState?> GetAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment authorisation request ID cannot be empty.",
                nameof(requestId));
        }

        var existing = await (
            from attempt in _dbContext.AuthorisationAttempts.AsNoTracking()
            join intent in _dbContext.PaymentIntents.AsNoTracking()
                on attempt.PaymentId equals intent.PaymentId
            where attempt.RequestId == requestId
            select new
            {
                Attempt = attempt,
                Intent = intent
            })
            .SingleOrDefaultAsync(cancellationToken);

        return existing is null
            ? null
            : ToState(existing.Intent, existing.Attempt);
    }

    public async Task<PaymentAuthorisationReconciliationDecision> ApplyAsync(
        PaymentAuthorisationReconciliation reconciliation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);

        if (reconciliation.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment authorisation request ID cannot be empty.",
                nameof(reconciliation));
        }

        if (!Enum.IsDefined(reconciliation.Status) ||
            reconciliation.Status == PaymentAuthorisationStatus.AuthorisationPending)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliation), reconciliation.Status,
                "Payment reconciliation must contain a non-pending outcome.");
        }

        if (reconciliation.Status is PaymentAuthorisationStatus.Authorised or
            PaymentAuthorisationStatus.Declined &&
            string.IsNullOrWhiteSpace(reconciliation.ProviderReference))
        {
            throw new ArgumentException(
                "A definite payment reconciliation outcome requires a provider reference.",
                nameof(reconciliation));
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var attempt = await _dbContext.AuthorisationAttempts
                                      .FromSqlInterpolated(
                                          $"""
                                          SELECT *
                                          FROM payments.authorisation_attempts
                                          WHERE request_id = {reconciliation.RequestId}
                                          FOR UPDATE
                                          """)
                                      .SingleOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            throw new PaymentAuthorisationNotFoundException(reconciliation.RequestId);
        }

        var intent = await _dbContext.PaymentIntents
                                     .SingleAsync(
                                         record => record.PaymentId == attempt.PaymentId,
                                         cancellationToken);

        EnsureConsistent(intent, attempt);

        if (attempt.Status != PaymentAuthorisationStatus.Indeterminate)
        {
            await transaction.CommitAsync(cancellationToken);

            return new PaymentAuthorisationReconciliationDecision(
                ToState(intent, attempt),
                Applied: false);
        }

        if (reconciliation.ReconciledAtUtc < attempt.RequestedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliation),
                "Payment reconciliation cannot occur before the authorisation request.");
        }

        attempt.ReconciliationAttemptCount++;
        attempt.LastReconciledAtUtc = reconciliation.ReconciledAtUtc;

        if (!string.IsNullOrWhiteSpace(reconciliation.ProviderReference))
        {
            attempt.ProviderReference = reconciliation.ProviderReference.Trim();
        }

        if (reconciliation.Status != PaymentAuthorisationStatus.Indeterminate)
        {
            attempt.Status = reconciliation.Status;
            attempt.ResolvedAtUtc = reconciliation.ReconciledAtUtc;

            intent.AuthorisationStatus = reconciliation.Status;
            intent.AuthorisationResolvedAtUtc = reconciliation.ReconciledAtUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PaymentAuthorisationReconciliationDecision(
            ToState(intent, attempt),
            Applied: true);
    }

    private static PaymentAuthorisationReconciliationState ToState(
        PaymentIntentRecord intent, PaymentAuthorisationAttemptRecord attempt)
    {
        EnsureConsistent(intent, attempt);

        return new PaymentAuthorisationReconciliationState(
            attempt.RequestId,
            intent.PaymentId,
            intent.OrderId,
            intent.Amount,
            intent.Currency,
            attempt.Status,
            attempt.ProviderIdempotencyKey,
            attempt.ProviderReference,
            attempt.RequestedAtUtc,
            attempt.ResolvedAtUtc,
            attempt.ReconciliationAttemptCount,
            attempt.LastReconciledAtUtc);
    }

    private static void EnsureConsistent(
        PaymentIntentRecord intent, PaymentAuthorisationAttemptRecord attempt)
    {
        if (intent.AuthorisationStatus != attempt.Status)
        {
            throw new InvalidOperationException(
                "Payment intent and authorisation attempt states are inconsistent.");
        }
    }
}
