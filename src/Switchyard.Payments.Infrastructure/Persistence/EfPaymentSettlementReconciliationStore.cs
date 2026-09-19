using Microsoft.EntityFrameworkCore;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.SettlementReconciliation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class EfPaymentSettlementReconciliationStore :
    IPaymentSettlementReconciliationStore
{
    private readonly PaymentsDbContext _dbContext;

    public EfPaymentSettlementReconciliationStore(PaymentsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PaymentSettlementReconciliationState?> GetAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment settlement request ID cannot be empty.",
                nameof(requestId));
        }

        var existing = await (
            from settlement in _dbContext.SettlementAttempts.AsNoTracking()
            join intent in _dbContext.PaymentIntents.AsNoTracking()
                on settlement.PaymentId equals intent.PaymentId
            join authorisation in _dbContext.AuthorisationAttempts.AsNoTracking()
                on settlement.PaymentId equals authorisation.PaymentId
            where settlement.RequestId == requestId
            select new
            {
                Settlement = settlement,
                Intent = intent,
                Authorisation = authorisation
            })
            .SingleOrDefaultAsync(cancellationToken);

        return existing is null
            ? null
            : ToState(
                existing.Intent,
                existing.Authorisation,
                existing.Settlement);
    }

    public async Task<PaymentSettlementReconciliationDecision> ApplyAsync(
        PaymentSettlementReconciliation reconciliation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);

        if (reconciliation.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment settlement request ID cannot be empty.",
                nameof(reconciliation));
        }

        if (!Enum.IsDefined(reconciliation.Status) ||
            reconciliation.Status == PaymentSettlementStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliation),
                reconciliation.Status,
                "Payment settlement reconciliation must contain a non-pending outcome.");
        }

        if (reconciliation.Status == PaymentSettlementStatus.Succeeded &&
            string.IsNullOrWhiteSpace(reconciliation.ProviderReference))
        {
            throw new ArgumentException(
                "A successful payment settlement reconciliation requires a provider reference.",
                nameof(reconciliation));
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var attempt = await _dbContext.SettlementAttempts
                                      .FromSqlInterpolated(
                                          $"""
                                          SELECT *
                                          FROM payments.settlement_attempts
                                          WHERE request_id = {reconciliation.RequestId}
                                          FOR UPDATE
                                          """)
                                      .SingleOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            throw new PaymentSettlementReconciliationNotFoundException(
                reconciliation.RequestId);
        }

        var intent = await _dbContext.PaymentIntents
                                     .SingleAsync(
                                         record => record.PaymentId == attempt.PaymentId,
                                         cancellationToken);
        var authorisation = await _dbContext.AuthorisationAttempts
                                                .SingleAsync(
                                                    record => record.PaymentId == attempt.PaymentId,
                                                    cancellationToken);

        EnsureConsistent(authorisation);

        if (attempt.Status == PaymentSettlementStatus.Pending)
        {
            throw new InvalidOperationException(
                "Pending payment settlement must be retried through settlement execution before reconciliation.");
        }

        if (reconciliation.ReconciledAtUtc < attempt.RequestedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliation),
                "Payment settlement reconciliation cannot occur before the settlement request.");
        }

        RecordReconciliationAttempt(attempt, reconciliation.ReconciledAtUtc);

        if (attempt.Status != PaymentSettlementStatus.Indeterminate)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PaymentSettlementReconciliationDecision(
                ToState(intent, authorisation, attempt),
                Applied: false);
        }

        if (!string.IsNullOrWhiteSpace(reconciliation.ProviderReference))
        {
            attempt.ProviderReference = reconciliation.ProviderReference.Trim();
        }

        if (reconciliation.Status != PaymentSettlementStatus.Indeterminate)
        {
            attempt.Status = reconciliation.Status;
            attempt.ResolvedAtUtc = reconciliation.ReconciledAtUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PaymentSettlementReconciliationDecision(
            ToState(intent, authorisation, attempt),
            Applied: true);
    }

    private static void RecordReconciliationAttempt(
        PaymentSettlementAttemptRecord attempt,
        DateTimeOffset reconciledAtUtc)
    {
        attempt.ReconciliationAttemptCount++;

        if (attempt.LastReconciledAtUtc is null ||
            reconciledAtUtc > attempt.LastReconciledAtUtc)
        {
            attempt.LastReconciledAtUtc = reconciledAtUtc;
        }
    }

    private static PaymentSettlementReconciliationState ToState(
        PaymentIntentRecord intent,
        PaymentAuthorisationAttemptRecord authorisation,
        PaymentSettlementAttemptRecord attempt)
    {
        EnsureConsistent(authorisation);

        return new PaymentSettlementReconciliationState(
            attempt.RequestId,
            attempt.PaymentId,
            intent.OrderId,
            attempt.Action,
            intent.Amount,
            intent.Currency,
            attempt.Status,
            attempt.ProviderIdempotencyKey,
            attempt.ProviderReference,
            authorisation.ProviderReference!,
            attempt.RequestedAtUtc,
            attempt.ResolvedAtUtc,
            attempt.ReconciliationAttemptCount,
            attempt.LastReconciledAtUtc);
    }

    private static void EnsureConsistent(
        PaymentAuthorisationAttemptRecord authorisation)
    {
        if (authorisation.Status != PaymentAuthorisationStatus.Authorised ||
            string.IsNullOrWhiteSpace(authorisation.ProviderReference))
        {
            throw new InvalidOperationException(
                "A payment settlement exists without a definite successful authorisation.");
        }
    }
}
