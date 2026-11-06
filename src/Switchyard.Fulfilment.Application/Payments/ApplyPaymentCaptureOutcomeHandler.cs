using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Application.Payments;

public sealed class ApplyPaymentCaptureOutcomeHandler
{
    private readonly IFulfilmentRequestRepository _repository;

    public ApplyPaymentCaptureOutcomeHandler(
        IFulfilmentRequestRepository repository)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ApplyPaymentCaptureOutcomeResult> HandleAsync(
        ApplyPaymentCaptureOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var transaction =
            await _repository.BeginTransactionAsync(
                cancellationToken);

        var request =
            await _repository.GetByOrderIdForUpdateAsync(
                command.OrderId,
                cancellationToken) ??
            throw new InvalidOperationException(
                $"Fulfilment request '{command.OrderId}' was not found.");

        var applied =
            request.ApplyPaymentCaptureOutcome(
                command.RequestId,
                command.PaymentId,
                command.Status,
                command.ProviderReference,
                command.ResolvedAtUtc);

        if (applied)
        {
            await _repository.SaveAsync(
                request,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return new ApplyPaymentCaptureOutcomeResult(
            Replayed: !applied,
            request.PaymentCaptureStatus);
    }
}

public sealed record ApplyPaymentCaptureOutcomeCommand(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    FulfilmentPaymentCaptureStatus Status,
    string? ProviderReference,
    DateTimeOffset ResolvedAtUtc);

public sealed record ApplyPaymentCaptureOutcomeResult(
    bool Replayed,
    FulfilmentPaymentCaptureStatus Status);
