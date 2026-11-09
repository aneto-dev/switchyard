using Microsoft.EntityFrameworkCore;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Refunds;
using Switchyard.Payments.Domain.Settlement;
using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class EfPaymentRefundRequestStore :
    IPaymentRefundRequestStore
{
    private readonly PaymentsDbContext _dbContext;

    public EfPaymentRefundRequestStore(
        PaymentsDbContext dbContext)
    {
        _dbContext =
            dbContext ??
            throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task<PaymentRefundRequestDecision> ApplyAsync(
        PaymentRefundRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var normalizedCurrency =
            request.Currency.Trim().ToUpperInvariant();

        var intent =
            await _dbContext.PaymentIntents
                            .FromSqlInterpolated(
                                $"""
                                SELECT *
                                FROM payments.payment_intents
                                WHERE payment_id = {request.PaymentId}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(
                                cancellationToken) ??
            throw new PaymentRefundNotAllowedException(
                request.PaymentId,
                "payment intent does not exist.");

        if (intent.OrderId != request.OrderId)
        {
            throw new PaymentRefundNotAllowedException(
                request.PaymentId,
                "order identity does not match the captured payment.");
        }

        if (!string.Equals(
                intent.Currency,
                normalizedCurrency,
                StringComparison.Ordinal))
        {
            throw new PaymentRefundNotAllowedException(
                request.PaymentId,
                "refund currency does not match the payment currency.");
        }

        var capture =
            await _dbContext.SettlementAttempts
                            .AsNoTracking()
                            .SingleOrDefaultAsync(
                                attempt =>
                                    attempt.PaymentId ==
                                        request.PaymentId &&
                                    attempt.Action ==
                                        PaymentSettlementAction.Capture,
                                cancellationToken);

        if (capture is null ||
            capture.Status !=
                PaymentSettlementStatus.Succeeded)
        {
            throw new PaymentRefundNotAllowedException(
                request.PaymentId,
                "payment has not been definitely captured.");
        }

        var existing =
            await _dbContext.RefundRequests
                            .SingleOrDefaultAsync(
                                candidate =>
                                    candidate.RequestId ==
                                    request.RequestId,
                                cancellationToken);

        if (existing is not null)
        {
            if (!Matches(
                    existing,
                    request,
                    normalizedCurrency))
            {
                throw new PaymentRefundConflictException(
                    request.RequestId);
            }

            return ToDecision(
                existing,
                Replayed: true);
        }

        var receiptConflict =
            await _dbContext.RefundRequests
                            .AsNoTracking()
                            .AnyAsync(
                                candidate =>
                                    candidate.ReturnReceiptId ==
                                    request.ReturnReceiptId,
                                cancellationToken);

        if (receiptConflict)
        {
            throw new PaymentRefundConflictException(
                request.RequestId);
        }

        var alreadyRequested =
            await _dbContext.RefundRequests
                            .Where(
                                candidate =>
                                    candidate.PaymentId ==
                                    request.PaymentId)
                            .SumAsync(
                                candidate =>
                                    candidate.Amount,
                                cancellationToken);

        if (alreadyRequested +
            request.Amount >
            intent.Amount)
        {
            throw new PaymentRefundNotAllowedException(
                request.PaymentId,
                "cumulative refunds would exceed the captured payment amount.");
        }

        var record =
            new PaymentRefundRequestRecord
            {
                RequestId = request.RequestId,
                PaymentId = request.PaymentId,
                OrderId = request.OrderId,
                ReturnRequestId = request.ReturnRequestId,
                ReturnReceiptId = request.ReturnReceiptId,
                Amount = request.Amount,
                Currency = normalizedCurrency,
                Status = PaymentRefundStatus.Pending,
                ProviderIdempotencyKey =
                    $"switchyard-refund-{request.RequestId:N}",
                ProviderReference = null,
                RequestedAtUtc = request.RequestedAtUtc.ToUniversalTime(),
                ProviderInvocationClaimedAtUtc = null,
                ResolvedAtUtc = null,
                ReconciliationAttemptCount = 0,
                LastReconciledAtUtc = null
            };

        await _dbContext.RefundRequests.AddAsync(
            record,
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new PaymentRefundConflictException(
                request.RequestId);
        }

        return ToDecision(
            record,
            Replayed: false);
    }

    private static void Validate(
        PaymentRefundRequest request)
    {
        if (request.RequestId == Guid.Empty ||
            request.PaymentId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            request.ReturnRequestId == Guid.Empty ||
            request.ReturnReceiptId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment refund identifiers are required.",
                nameof(request));
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Payment refund amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(
                request.Currency) ||
            request.Currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "Payment refund currency must contain exactly three characters.",
                nameof(request));
        }

        if (request.RequestedAtUtc == default)
        {
            throw new ArgumentException(
                "Payment refund request time is required.",
                nameof(request));
        }
    }

    private static bool Matches(
        PaymentRefundRequestRecord record,
        PaymentRefundRequest request,
        string normalizedCurrency) =>
        record.PaymentId == request.PaymentId &&
        record.OrderId == request.OrderId &&
        record.ReturnRequestId == request.ReturnRequestId &&
        record.ReturnReceiptId == request.ReturnReceiptId &&
        record.Amount == request.Amount &&
        string.Equals(
            record.Currency,
            normalizedCurrency,
            StringComparison.Ordinal) &&
        record.RequestedAtUtc ==
            request.RequestedAtUtc.ToUniversalTime();

    private static PaymentRefundRequestDecision ToDecision(
        PaymentRefundRequestRecord record,
        bool Replayed) =>
        new(
            record.RequestId,
            record.PaymentId,
            record.OrderId,
            record.Amount,
            record.Currency,
            Replayed);
}
