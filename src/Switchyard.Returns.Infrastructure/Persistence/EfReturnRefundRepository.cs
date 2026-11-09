using Microsoft.EntityFrameworkCore;
using Switchyard.Returns.Application.Refunds;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class EfReturnRefundRepository :
    IReturnRefundRepository
{
    private readonly ReturnsDbContext _dbContext;

    public EfReturnRefundRepository(
        ReturnsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task CreatePendingAsync(
        ReturnRefundProjection refund,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            refund);

        ValidateIdentity(
            refund.OrderId,
            refund.ReturnRequestId,
            refund.ReturnReceiptId,
            refund.PaymentId,
            refund.Amount,
            refund.Currency);

        if (refund.Status !=
            ReturnRefundStatus.Pending)
        {
            throw new ArgumentException(
                "A newly created return refund must be pending.",
                nameof(refund));
        }

        var normalizedCurrency =
            refund.Currency.Trim().ToUpperInvariant();

        var existing =
            await _dbContext.ReturnRefunds
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.OrderId ==
                                        refund.OrderId &&
                                    candidate.ReturnReceiptId ==
                                        refund.ReturnReceiptId,
                                cancellationToken);

        if (existing is not null)
        {
            if (!MatchesIdentity(
                    existing,
                    refund.ReturnRequestId,
                    refund.PaymentId,
                    refund.Amount,
                    normalizedCurrency))
            {
                throw new ReturnRefundConflictException(
                    refund.OrderId,
                    refund.ReturnReceiptId);
            }

            return;
        }

        await _dbContext.ReturnRefunds.AddAsync(
            new ReturnRefundRecord
            {
                OrderId = refund.OrderId,
                ReturnReceiptId = refund.ReturnReceiptId,
                ReturnRequestId = refund.ReturnRequestId,
                PaymentId = refund.PaymentId,
                Amount = refund.Amount,
                Currency = normalizedCurrency,
                Status = (int)ReturnRefundStatus.Pending,
                ProviderReference = null,
                RequestedAtUtc = refund.RequestedAtUtc.ToUniversalTime(),
                UpdatedAtUtc = refund.UpdatedAtUtc.ToUniversalTime()
            },
            cancellationToken);
    }

    public async Task<ApplyReturnRefundOutcomeResult> ApplyOutcomeAsync(
        ApplyReturnRefundOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            outcome);

        ValidateIdentity(
            outcome.OrderId,
            outcome.ReturnRequestId,
            outcome.ReturnReceiptId,
            outcome.PaymentId,
            outcome.Amount,
            outcome.Currency);

        if (outcome.Status ==
            ReturnRefundStatus.Pending)
        {
            throw new ArgumentException(
                "A refund outcome cannot transition to pending.",
                nameof(outcome));
        }

        var normalizedCurrency =
            outcome.Currency.Trim().ToUpperInvariant();

        var record =
            await _dbContext.ReturnRefunds
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.OrderId ==
                                        outcome.OrderId &&
                                    candidate.ReturnReceiptId ==
                                        outcome.ReturnReceiptId,
                                cancellationToken) ??
            throw new ReturnRefundNotFoundException(
                outcome.OrderId,
                outcome.ReturnReceiptId);

        if (!MatchesIdentity(
                record,
                outcome.ReturnRequestId,
                outcome.PaymentId,
                outcome.Amount,
                normalizedCurrency))
        {
            throw new ReturnRefundConflictException(
                outcome.OrderId,
                outcome.ReturnReceiptId);
        }

        var incomingProviderReference =
            string.IsNullOrWhiteSpace(
                outcome.ProviderReference)
                ? null
                : outcome.ProviderReference.Trim();

        if (record.ProviderReference is not null &&
            incomingProviderReference is not null &&
            !string.Equals(
                record.ProviderReference,
                incomingProviderReference,
                StringComparison.Ordinal))
        {
            throw new ReturnRefundConflictException(
                outcome.OrderId,
                outcome.ReturnReceiptId);
        }

        var occurredAtUtc =
            outcome.OccurredAtUtc.ToUniversalTime();

        ArgumentOutOfRangeException.ThrowIfLessThan(
            occurredAtUtc,
            record.RequestedAtUtc);

        var currentStatus =
            (ReturnRefundStatus)record.Status;

        if (currentStatus is
            ReturnRefundStatus.Succeeded or
            ReturnRefundStatus.NotApplied)
        {
            if (outcome.Status ==
                ReturnRefundStatus.Indeterminate)
            {
                return new ApplyReturnRefundOutcomeResult(
                    currentStatus,
                    Replayed: true);
            }

            if (outcome.Status !=
                currentStatus)
            {
                throw new ReturnRefundConflictException(
                    outcome.OrderId,
                    outcome.ReturnReceiptId);
            }

            return new ApplyReturnRefundOutcomeResult(
                currentStatus,
                Replayed: true);
        }

        if (currentStatus ==
                outcome.Status &&
            record.UpdatedAtUtc ==
                outcome.OccurredAtUtc.ToUniversalTime() &&
            (record.ProviderReference is not null ||
             incomingProviderReference is null))
        {
            return new ApplyReturnRefundOutcomeResult(
                currentStatus,
                Replayed: true);
        }

        if (currentStatus ==
                ReturnRefundStatus.Indeterminate &&
            outcome.Status ==
                ReturnRefundStatus.Pending)
        {
            throw new ReturnRefundConflictException(
                outcome.OrderId,
                outcome.ReturnReceiptId);
        }

        record.Status =
            (int)outcome.Status;

        if (incomingProviderReference is not null)
        {
            record.ProviderReference =
                incomingProviderReference;
        }

        record.UpdatedAtUtc =
            occurredAtUtc;

        return new ApplyReturnRefundOutcomeResult(
            outcome.Status,
            Replayed: false);
    }

    private static void ValidateIdentity(
        Guid orderId,
        Guid returnRequestId,
        Guid returnReceiptId,
        Guid paymentId,
        decimal amount,
        string currency)
    {
        if (orderId == Guid.Empty ||
            returnRequestId == Guid.Empty ||
            returnReceiptId == Guid.Empty ||
            paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Return refund identifiers are required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            amount);

        if (string.IsNullOrWhiteSpace(
                currency) ||
            currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "Return refund currency must contain exactly three characters.",
                nameof(currency));
        }
    }

    private static bool MatchesIdentity(
        ReturnRefundRecord record,
        Guid returnRequestId,
        Guid paymentId,
        decimal amount,
        string currency) =>
        record.ReturnRequestId == returnRequestId &&
        record.PaymentId == paymentId &&
        record.Amount == amount &&
        string.Equals(
            record.Currency,
            currency,
            StringComparison.Ordinal);
}
