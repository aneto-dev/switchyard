using System.Collections.Concurrent;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Application.RefundReconciliation;
using Switchyard.Payments.Application.Refunds;

namespace Switchyard.Payments.Infrastructure.ProviderSimulation;

public sealed class SimulatedPaymentRefundProvider :
    IPaymentRefundProvider,
    IPaymentRefundReconciliationProvider
{
    private readonly SimulatedPaymentRefundScenario _scenario;
    private readonly SimulatedPaymentRefundReconciliationScenario _reconciliationScenario;
    private readonly ConcurrentDictionary<string, SimulatedRefund> _refunds =
        new(StringComparer.Ordinal);

    private int _invocationCount;
    private int _reconciliationInvocationCount;

    public SimulatedPaymentRefundProvider(
        SimulatedPaymentRefundScenario scenario,
        SimulatedPaymentRefundReconciliationScenario reconciliationScenario)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scenario));
        }

        if (!Enum.IsDefined(reconciliationScenario))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliationScenario));
        }

        _scenario = scenario;
        _reconciliationScenario = reconciliationScenario;
    }

    public int InvocationCount =>
        Volatile.Read(ref _invocationCount);

    public int ReconciliationInvocationCount =>
        Volatile.Read(
            ref _reconciliationInvocationCount);

    public int UniqueRefundCount =>
        _refunds.Count;

    public Task<PaymentProviderRefundResult> RefundAsync(
        PaymentProviderRefundRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(
                request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Refund provider idempotency key is required.",
                nameof(request));
        }

        Interlocked.Increment(
            ref _invocationCount);

        var candidate =
            new SimulatedRefund(
                request.RequestId,
                request.PaymentId,
                request.OrderId,
                request.Amount,
                request.Currency,
                $"sim-refund-{Guid.NewGuid():N}");

        var stored =
            _refunds.GetOrAdd(
                request.IdempotencyKey,
                candidate);

        if (stored.RequestId != request.RequestId ||
            stored.PaymentId != request.PaymentId ||
            stored.OrderId != request.OrderId ||
            stored.Amount != request.Amount ||
            !string.Equals(
                stored.Currency,
                request.Currency,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refund provider idempotency key was reused with different refund data.");
        }

        if (_scenario ==
            SimulatedPaymentRefundScenario.Indeterminate)
        {
            throw new PaymentRefundProviderIndeterminateException(
                stored.ProviderReference);
        }

        return Task.FromResult(
            new PaymentProviderRefundResult(
                stored.ProviderReference));
    }

    public Task<PaymentProviderRefundReconciliationResult> ReconcileAsync(
        PaymentProviderRefundReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(
                request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Refund reconciliation idempotency key is required.",
                nameof(request));
        }

        Interlocked.Increment(
            ref _reconciliationInvocationCount);

        if (!_refunds.TryGetValue(
                request.IdempotencyKey,
                out var stored))
        {
            return Task.FromResult(
                new PaymentProviderRefundReconciliationResult(
                    PaymentProviderRefundReconciliationOutcome.NotApplied,
                    null));
        }

        if (stored.RequestId != request.RequestId ||
            stored.PaymentId != request.PaymentId ||
            stored.OrderId != request.OrderId ||
            stored.Amount != request.Amount ||
            !string.Equals(
                stored.Currency,
                request.Currency,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refund reconciliation identity does not match the stored provider refund.");
        }

        var result =
            _reconciliationScenario switch
            {
                SimulatedPaymentRefundReconciliationScenario.Succeed =>
                    new PaymentProviderRefundReconciliationResult(
                        PaymentProviderRefundReconciliationOutcome.Succeeded,
                        stored.ProviderReference),
                SimulatedPaymentRefundReconciliationScenario.NotApplied =>
                    new PaymentProviderRefundReconciliationResult(
                        PaymentProviderRefundReconciliationOutcome.NotApplied,
                        null),
                SimulatedPaymentRefundReconciliationScenario.StillIndeterminate =>
                    new PaymentProviderRefundReconciliationResult(
                        PaymentProviderRefundReconciliationOutcome.Unknown,
                        request.ProviderReference),
                _ =>
                    throw new InvalidOperationException(
                        "Refund reconciliation simulation reached an unsupported scenario.")
            };

        return Task.FromResult(result);
    }

    private sealed record SimulatedRefund(
        Guid RequestId,
        Guid PaymentId,
        Guid OrderId,
        decimal Amount,
        string Currency,
        string ProviderReference);
}
