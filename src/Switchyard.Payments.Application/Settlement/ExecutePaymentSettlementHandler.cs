using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Application.Settlement;

public sealed class ExecutePaymentSettlementHandler
{
    private readonly IPaymentSettlementStore _store;
    private readonly IPaymentSettlementProvider _provider;
    private readonly TimeProvider _timeProvider;

    public ExecutePaymentSettlementHandler(
        IPaymentSettlementStore store,
        IPaymentSettlementProvider provider,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ExecutePaymentSettlementResult> HandleAsync(
        ExecutePaymentSettlementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment settlement request ID cannot be empty.",
                nameof(command));
        }

        if (command.PaymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment ID cannot be empty.",
                nameof(command));
        }

        if (!Enum.IsDefined(command.Action))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command), command.Action,
                "Payment settlement action is not supported.");
        }

        var started = await _store.BeginAsync(
            new PaymentSettlementRequest(
                command.RequestId,
                command.PaymentId,
                command.Action,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        if (started.Attempt.Status != PaymentSettlementStatus.Pending)
        {
            return ToResult(started, started.Replayed);
        }

        PaymentSettlementCompletion completion;

        try
        {
            var providerResult = await _provider.ExecuteAsync(
                new PaymentProviderSettlementRequest(
                    started.Attempt.ProviderIdempotencyKey,
                    started.Attempt.PaymentId.Value,
                    started.OrderId,
                    started.Attempt.Action,
                    started.Amount,
                    started.Currency,
                    started.AuthorisationProviderReference),
                cancellationToken);

            completion = new PaymentSettlementCompletion(
                command.RequestId,
                PaymentSettlementStatus.Succeeded,
                providerResult.ProviderReference,
                _timeProvider.GetUtcNow());
        }
        catch (PaymentSettlementProviderIndeterminateException exception)
        {
            completion = new PaymentSettlementCompletion(
                command.RequestId,
                PaymentSettlementStatus.Indeterminate,
                exception.ProviderReference,
                _timeProvider.GetUtcNow());
        }

        var completed = await _store.CompleteAsync(completion, cancellationToken);

        return ToResult(completed, started.Replayed || completed.Replayed);
    }

    private static ExecutePaymentSettlementResult ToResult(
        PaymentSettlementDecision decision, bool replayed)
    {
        return new ExecutePaymentSettlementResult(
            decision.Attempt.RequestId,
            decision.Attempt.PaymentId.Value,
            decision.OrderId,
            decision.Attempt.Action,
            decision.Attempt.Status,
            decision.Attempt.ProviderIdempotencyKey,
            decision.Attempt.ProviderReference,
            decision.Attempt.RequestedAtUtc,
            decision.Attempt.ResolvedAtUtc,
            replayed);
    }
}
