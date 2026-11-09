using Switchyard.Payments.Application.RefundExecution;

namespace Switchyard.Payments.Application.Ports;

public interface IPaymentRefundExecutionStore
{
    Task<IReadOnlyList<Guid>> GetPendingRequestIdsAsync(
        int batchSize,
        CancellationToken cancellationToken);

    Task<PaymentRefundProviderInvocationClaim> ClaimProviderInvocationAsync(
        Guid requestId,
        DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentRefundExecutionDecision> CompleteAsync(
        PaymentRefundExecutionCompletion completion,
        Func<PaymentRefundExecutionState, CancellationToken, Task>? onApplied,
        CancellationToken cancellationToken);
}
