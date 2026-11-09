using Switchyard.Messaging;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

internal static class ReturnsRefundOutcomeRouteHandler
{
    public static async Task ApplyAsync(
        ReturnsDbContext dbContext,
        IntegrationMessageEnvelope message,
        ParsedRefundOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.RequestId == Guid.Empty ||
            outcome.OrderId == Guid.Empty ||
            outcome.PaymentId == Guid.Empty ||
            outcome.ReturnRequestId == Guid.Empty ||
            outcome.ReturnReceiptId == Guid.Empty ||
            outcome.RequestId != outcome.ReturnReceiptId ||
            outcome.Amount <= 0 ||
            string.IsNullOrWhiteSpace(outcome.Currency) ||
            outcome.Currency.Trim().Length != 3 ||
            outcome.OccurredAtUtc == default ||
            message.CorrelationId != outcome.OrderId ||
            message.CausationId != outcome.RequestId ||
            message.OccurredAtUtc != outcome.OccurredAtUtc)
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment refund outcome contains invalid required fields, correlation or causation.");
        }

        var repository =
            new EfReturnRefundRepository(
                dbContext);

        try
        {
            await repository.ApplyOutcomeAsync(
                new ApplyReturnRefundOutcome(
                    outcome.OrderId,
                    outcome.ReturnRequestId,
                    outcome.ReturnReceiptId,
                    outcome.PaymentId,
                    outcome.Amount,
                    outcome.Currency,
                    outcome.Status,
                    outcome.ProviderReference,
                    outcome.OccurredAtUtc),
                cancellationToken);
        }
        catch (ReturnRefundNotFoundException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
        catch (ReturnRefundConflictException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                exception.Message);
        }
    }
}

internal sealed record ParsedRefundOutcome(
    Guid RequestId,
    Guid OrderId,
    Guid PaymentId,
    Guid ReturnRequestId,
    Guid ReturnReceiptId,
    decimal Amount,
    string Currency,
    ReturnRefundStatus Status,
    string? ProviderReference,
    DateTimeOffset OccurredAtUtc);
