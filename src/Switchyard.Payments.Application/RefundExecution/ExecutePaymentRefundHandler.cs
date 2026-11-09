using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.Refunds;
using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Payments.Application.RefundExecution;

public sealed class ExecutePaymentRefundHandler
{
    private readonly IPaymentRefundExecutionStore _store;
    private readonly IPaymentRefundProvider _provider;
    private readonly TimeProvider _timeProvider;

    public ExecutePaymentRefundHandler(
        IPaymentRefundExecutionStore store,
        IPaymentRefundProvider provider,
        TimeProvider timeProvider)
    {
        _store =
            store ??
            throw new ArgumentNullException(nameof(store));

        _provider =
            provider ??
            throw new ArgumentNullException(nameof(provider));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PaymentRefundExecutionDecision> HandleAsync(
        Guid requestId,
        Func<PaymentRefundExecutionState, CancellationToken, Task>? onApplied,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Refund request ID cannot be empty.",
                nameof(requestId));
        }

        var claim =
            await _store.ClaimProviderInvocationAsync(
                requestId,
                _timeProvider.GetUtcNow(),
                cancellationToken);

        if (claim.State.Status !=
            PaymentRefundStatus.Pending)
        {
            return new PaymentRefundExecutionDecision(
                claim.State,
                Applied: false);
        }

        if (!claim.Acquired)
        {
            return await _store.CompleteAsync(
                new PaymentRefundExecutionCompletion(
                    requestId,
                    PaymentRefundStatus.Indeterminate,
                    claim.State.ProviderReference,
                    _timeProvider.GetUtcNow()),
                onApplied,
                cancellationToken);
        }

        PaymentRefundExecutionCompletion completion;

        try
        {
            var providerResult =
                await _provider.RefundAsync(
                    new PaymentProviderRefundRequest(
                        claim.State.ProviderIdempotencyKey,
                        claim.State.RequestId,
                        claim.State.PaymentId,
                        claim.State.OrderId,
                        claim.State.Amount,
                        claim.State.Currency),
                    cancellationToken);

            completion =
                new PaymentRefundExecutionCompletion(
                    requestId,
                    PaymentRefundStatus.Succeeded,
                    providerResult.ProviderReference,
                    _timeProvider.GetUtcNow());
        }
        catch (PaymentRefundProviderIndeterminateException exception)
        {
            completion =
                new PaymentRefundExecutionCompletion(
                    requestId,
                    PaymentRefundStatus.Indeterminate,
                    exception.ProviderReference,
                    _timeProvider.GetUtcNow());
        }

        return await _store.CompleteAsync(
            completion,
            onApplied,
            cancellationToken);
    }
}
