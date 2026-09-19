using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class EfPaymentAuthorisationStore : IPaymentAuthorisationStore
{
    private readonly PaymentsDbContext _dbContext;

    public EfPaymentAuthorisationStore(PaymentsDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PaymentAuthorisationDecision> BeginAsync(
        PaymentAuthorisationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await GetExistingDecisionAsync(request, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var existingIntent = await _dbContext.PaymentIntents.AsNoTracking()
                                                    .SingleOrDefaultAsync(
                                                        record => record.OrderId == request.OrderId,
                                                        cancellationToken);

        if (existingIntent is not null)
        {
            throw new PaymentAuthorisationConflictException(request.RequestId, request.OrderId);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var paymentId = PaymentId.New();
            var providerIdempotencyKey = BuildProviderIdempotencyKey(request.RequestId);

            var intent = new PaymentIntentRecord
            {
                PaymentId = paymentId.Value,
                OrderId = request.OrderId,
                Amount = request.Amount.Amount,
                Currency = request.Amount.Currency,
                AuthorisationStatus = PaymentAuthorisationStatus.AuthorisationPending,
                CreatedAtUtc = request.RequestedAtUtc,
                AuthorisationResolvedAtUtc = null
            };

            var attempt = new PaymentAuthorisationAttemptRecord
            {
                RequestId = request.RequestId,
                PaymentId = paymentId.Value,
                ProviderIdempotencyKey = providerIdempotencyKey,
                ProviderReference = null,
                Status = PaymentAuthorisationStatus.AuthorisationPending,
                RequestedAtUtc = request.RequestedAtUtc,
                ResolvedAtUtc = null
            };

            await _dbContext.PaymentIntents.AddAsync(intent, cancellationToken);
            await _dbContext.AuthorisationAttempts.AddAsync(attempt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToDecision(intent, attempt, replayed: false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var concurrent = await GetExistingDecisionAsync(request, cancellationToken);

            if (concurrent is not null)
            {
                return concurrent;
            }

            var concurrentIntent = await _dbContext.PaymentIntents.AsNoTracking()
                                                       .SingleOrDefaultAsync(
                                                           record => record.OrderId == request.OrderId,
                                                           cancellationToken);

            if (concurrentIntent is not null)
            {
                throw new PaymentAuthorisationConflictException(
                    request.RequestId,
                    request.OrderId);
            }

            throw new InvalidOperationException(
                "The concurrent payment authorisation could not be resolved after the database conflict.",
                exception);
        }
    }

    public async Task<PaymentAuthorisationDecision> CompleteAsync(
        PaymentAuthorisationCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (completion.Status == PaymentAuthorisationStatus.AuthorisationPending ||
            !Enum.IsDefined(completion.Status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(completion), completion.Status,
                "Payment authorisation completion must contain a terminal outcome.");
        }

        if (completion.Status is PaymentAuthorisationStatus.Authorised or PaymentAuthorisationStatus.Declined &&
            string.IsNullOrWhiteSpace(completion.ProviderReference))
        {
            throw new ArgumentException(
                "A definite payment authorisation outcome requires a provider reference.",
                nameof(completion));
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var attempt = await _dbContext.AuthorisationAttempts
                                      .FromSqlInterpolated(
                                          $"""
                                          SELECT *
                                          FROM payments.authorisation_attempts
                                          WHERE request_id = {completion.RequestId}
                                          FOR UPDATE
                                          """)
                                      .SingleOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            throw new InvalidOperationException(
                $"Payment authorisation request '{completion.RequestId}' does not exist.");
        }

        var intent = await _dbContext.PaymentIntents
                                     .SingleAsync(
                                         record => record.PaymentId == attempt.PaymentId,
                                         cancellationToken);

        if (attempt.Status != PaymentAuthorisationStatus.AuthorisationPending)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToDecision(intent, attempt, replayed: true);
        }

        if (intent.AuthorisationStatus != PaymentAuthorisationStatus.AuthorisationPending)
        {
            throw new InvalidOperationException(
                "Payment intent and authorisation attempt states are inconsistent.");
        }

        attempt.Status = completion.Status;
        attempt.ProviderReference = string.IsNullOrWhiteSpace(completion.ProviderReference)
            ? null
            : completion.ProviderReference.Trim();
        attempt.ResolvedAtUtc = completion.ResolvedAtUtc;

        intent.AuthorisationStatus = completion.Status;
        intent.AuthorisationResolvedAtUtc = completion.ResolvedAtUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDecision(intent, attempt, replayed: false);
    }

    private async Task<PaymentAuthorisationDecision?> GetExistingDecisionAsync(
        PaymentAuthorisationRequest request, CancellationToken cancellationToken)
    {
        var existing = await (
            from attempt in _dbContext.AuthorisationAttempts.AsNoTracking()
            join intent in _dbContext.PaymentIntents.AsNoTracking()
                on attempt.PaymentId equals intent.PaymentId
            where attempt.RequestId == request.RequestId
            select new
            {
                Attempt = attempt,
                Intent = intent
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.Intent.OrderId != request.OrderId ||
            existing.Intent.Amount != request.Amount.Amount ||
            !string.Equals(
                existing.Intent.Currency,
                request.Amount.Currency,
                StringComparison.Ordinal))
        {
            throw new PaymentAuthorisationConflictException(request.RequestId, request.OrderId);
        }

        return ToDecision(existing.Intent, existing.Attempt, replayed: true);
    }

    private static PaymentAuthorisationDecision ToDecision(
        PaymentIntentRecord intent, PaymentAuthorisationAttemptRecord attempt,
        bool replayed)
    {
        if (intent.AuthorisationStatus != attempt.Status)
        {
            throw new InvalidOperationException(
                "Payment intent and authorisation attempt states are inconsistent.");
        }

        var paymentId = new PaymentId(intent.PaymentId);
        var paymentIntent = new PaymentIntent(
            paymentId,
            intent.OrderId,
            new PaymentAmount(intent.Amount, intent.Currency),
            intent.AuthorisationStatus,
            intent.CreatedAtUtc,
            intent.AuthorisationResolvedAtUtc);

        var authorisationAttempt = new PaymentAuthorisationAttempt(
            attempt.RequestId,
            paymentId,
            attempt.ProviderIdempotencyKey,
            attempt.ProviderReference,
            attempt.Status,
            attempt.RequestedAtUtc,
            attempt.ResolvedAtUtc);

        return new PaymentAuthorisationDecision(
            paymentIntent,
            authorisationAttempt,
            replayed);
    }

    private static string BuildProviderIdempotencyKey(Guid requestId) =>
        $"switchyard-auth-{requestId:N}";

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
}
