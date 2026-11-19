using System.Text.Json;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class PaymentRefundedReturnsInboundMessageRoute :
    IReturnsInboundMessageRoute
{
    public string MessageType =>
        PaymentRefundedV1.MessageType;

    public async Task HandleAsync(
        ReturnsDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        PaymentRefundedV1 payload;

        try
        {
            payload =
                JsonSerializer.Deserialize<PaymentRefundedV1>(
                    message.PayloadJson,
                    JsonSerializerOptions.Web) ??
                throw new JsonException(
                    "Payment refunded payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Payment refunded payload is invalid: {exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(
                payload.ProviderReference))
        {
            throw new NonRetryableIntegrationMessageException(
                "Successful payment refund requires a provider reference.");
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
                ReturnRefundStatus.Succeeded,
                payload.ProviderReference,
                payload.RefundedAtUtc),
            cancellationToken);
    }
}
