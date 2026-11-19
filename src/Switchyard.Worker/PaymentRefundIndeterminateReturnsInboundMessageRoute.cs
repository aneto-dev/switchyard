using System.Text.Json;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class PaymentRefundIndeterminateReturnsInboundMessageRoute :
    IReturnsInboundMessageRoute
{
    public string MessageType =>
        PaymentRefundIndeterminateV1.MessageType;

    public async Task HandleAsync(
        ReturnsDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        PaymentRefundIndeterminateV1 payload;

        try
        {
            payload =
                JsonSerializer.Deserialize<PaymentRefundIndeterminateV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Payment refund indeterminate payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Payment refund indeterminate payload is invalid: {exception.Message}");
        }

        await ReturnsRefundOutcomeRouteHandler.ApplyAsync(
            dbContext,
            message,
            new ParsedRefundOutcome(
                payload.RequestId,
                payload.OrderId,
                payload.PaymentId,
                payload.ReturnRequestId,
                payload.ReturnReceiptId,
                payload.Amount,
                payload.Currency,
                ReturnRefundStatus.Indeterminate,
                payload.ProviderReference,
                payload.DetectedAtUtc),
            cancellationToken);
    }
}
