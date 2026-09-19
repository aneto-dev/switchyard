using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Settlement;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;
using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class EfPaymentSettlementStore : IPaymentSettlementStore
{
    private readonly PaymentsDbContext _dbContext;

    public EfPaymentSettlementStore(PaymentsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PaymentSettlementDecision> BeginAsync(
        PaymentSettlementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment settlement request ID cannot be empty.",
                nameof(request));
        }

        if (request.PaymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment ID cannot be empty.", nameof(request));
        }

        if (!Enum.IsDefined(request.Action))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), request.Action,
                "Payment settlement action is not supported.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var intent = await _dbContext.PaymentIntents
                                         .FromSqlInterpolated(
                                             $"""
                                             SELECT *
                                             FROM payments.payment_intents
                                             WHERE payment_id = {request.PaymentId}
                                             FOR UPDATE
                                             """)
                                         .SingleOrDefaultAsync(cancellationToken);

            if (intent is null)
            {
                throw new PaymentSettlementNotFoundException(request.PaymentId);
            }

            var authorisation = await _dbContext.AuthorisationAttempts
                                                .SingleAsync(
                                                    record => record.PaymentId == request.PaymentId,
                                                    cancellationToken);

            if (authorisation.Status != PaymentAuthorisationStatus.Authorised ||
                string.IsNullOrWhiteSpace(authorisation.ProviderReference))
            {
                throw new PaymentSettlementNotAllowedException(
                    request.PaymentId,
                    authorisation.Status);
            }

            var existingByRequest = await _dbContext.SettlementAttempts
                                                    .SingleOrDefaultAsync(
                                                        record => record.RequestId == request.RequestId,
                                                        cancellationToken);

            if (existingByRequest is not null)
            {
                if (existingByRequest.PaymentId != request.PaymentId ||
                    existingByRequest.Action != request.Action)
                {
                    throw new PaymentSettlementConflictException(
                        request.RequestId,
                        request.PaymentId,
                        request.Action);
                }

                await transaction.CommitAsync(cancellationToken);

                return ToDecision(
                    intent,
                    authorisation,
                    existingByRequest,
                    replayed: true);
            }

            var existingForPayment = await _dbContext.SettlementAttempts
                                                     .SingleOrDefaultAsync(
                                                         record => record.PaymentId == request.PaymentId,
                                                         cancellationToken);

            if (existingForPayment is not null)
            {
                throw new PaymentSettlementConflictException(
                    request.RequestId,
                    request.PaymentId,
                    request.Action);
            }

            var attempt = new PaymentSettlementAttemptRecord
            {
                RequestId = request.RequestId,
                PaymentId = request.PaymentId,
                Action = request.Action,
                Status = PaymentSettlementStatus.Pending,
                ProviderIdempotencyKey = BuildProviderIdempotencyKey(
                    request.Action,
                    request.RequestId),
                ProviderReference = null,
                RequestedAtUtc = request.RequestedAtUtc,
                ResolvedAtUtc = null
            };

            await _dbContext.SettlementAttempts.AddAsync(attempt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToDecision(intent, authorisation, attempt, replayed: false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var existing = await GetExistingDecisionAsync(request, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            throw new PaymentSettlementConflictException(
                request.RequestId,
                request.PaymentId,
                request.Action);
        }
    }

    public async Task<PaymentSettlementDecision> CompleteAsync(
        PaymentSettlementCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (completion.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment settlement request ID cannot be empty.",
                nameof(completion));
        }

        if (!Enum.IsDefined(completion.Status) ||
            completion.Status == PaymentSettlementStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completion), completion.Status,
                "Payment settlement completion must contain a non-pending outcome.");
        }

        if (completion.Status == PaymentSettlementStatus.Succeeded &&
            string.IsNullOrWhiteSpace(completion.ProviderReference))
        {
            throw new ArgumentException(
                "A successful payment settlement requires a provider reference.",
                nameof(completion));
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var attempt = await _dbContext.SettlementAttempts
                                      .FromSqlInterpolated(
                                          $"""
                                          SELECT *
                                          FROM payments.settlement_attempts
                                          WHERE request_id = {completion.RequestId}
                                          FOR UPDATE
                                          """)
                                      .SingleOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            throw new InvalidOperationException(
                $"Payment settlement request '{completion.RequestId}' does not exist.");
        }

        var intent = await _dbContext.PaymentIntents
                                     .SingleAsync(
                                         record => record.PaymentId == attempt.PaymentId,
                                         cancellationToken);
        var authorisation = await _dbContext.AuthorisationAttempts
                                                .SingleAsync(
                                                    record => record.PaymentId == attempt.PaymentId,
                                                    cancellationToken);

        if (attempt.Status != PaymentSettlementStatus.Pending)
        {
            await transaction.CommitAsync(cancellationToken);

            return ToDecision(intent, authorisation, attempt, replayed: true);
        }

        if (completion.ResolvedAtUtc < attempt.RequestedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completion),
                "Payment settlement cannot resolve before it was requested.");
        }

        attempt.Status = completion.Status;
        attempt.ProviderReference = string.IsNullOrWhiteSpace(completion.ProviderReference)
            ? null
            : completion.ProviderReference.Trim();
        attempt.ResolvedAtUtc = completion.ResolvedAtUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDecision(intent, authorisation, attempt, replayed: false);
    }

    private async Task<PaymentSettlementDecision?> GetExistingDecisionAsync(
        PaymentSettlementRequest request, CancellationToken cancellationToken)
    {
        var existing = await (
            from settlement in _dbContext.SettlementAttempts.AsNoTracking()
            join intent in _dbContext.PaymentIntents.AsNoTracking()
                on settlement.PaymentId equals intent.PaymentId
            join authorisation in _dbContext.AuthorisationAttempts.AsNoTracking()
                on settlement.PaymentId equals authorisation.PaymentId
            where settlement.RequestId == request.RequestId
            select new
            {
                Settlement = settlement,
                Intent = intent,
                Authorisation = authorisation
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.Settlement.PaymentId != request.PaymentId ||
            existing.Settlement.Action != request.Action)
        {
            throw new PaymentSettlementConflictException(
                request.RequestId,
                request.PaymentId,
                request.Action);
        }

        return ToDecision(
            existing.Intent,
            existing.Authorisation,
            existing.Settlement,
            replayed: true);
    }

    private static PaymentSettlementDecision ToDecision(
        PaymentIntentRecord intent,
        PaymentAuthorisationAttemptRecord authorisation,
        PaymentSettlementAttemptRecord attempt,
        bool replayed)
    {
        if (authorisation.Status != PaymentAuthorisationStatus.Authorised ||
            string.IsNullOrWhiteSpace(authorisation.ProviderReference))
        {
            throw new InvalidOperationException(
                "A payment settlement exists without a definite successful authorisation.");
        }

        var settlementAttempt = new PaymentSettlementAttempt(
            attempt.RequestId,
            new PaymentId(attempt.PaymentId),
            attempt.Action,
            attempt.Status,
            attempt.ProviderIdempotencyKey,
            attempt.ProviderReference,
            attempt.RequestedAtUtc,
            attempt.ResolvedAtUtc);

        return new PaymentSettlementDecision(
            settlementAttempt,
            intent.OrderId,
            intent.Amount,
            intent.Currency,
            authorisation.ProviderReference,
            replayed);
    }

    private static string BuildProviderIdempotencyKey(
        PaymentSettlementAction action, Guid requestId)
    {
        var actionName = action == PaymentSettlementAction.Capture
            ? "capture"
            : "void";

        return $"switchyard-{actionName}-{requestId:N}";
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
}
