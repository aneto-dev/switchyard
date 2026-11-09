using System.Text.Json;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Payments.Application.RefundExecution;
using Switchyard.Payments.Domain.Refunds;

namespace Switchyard.Worker;

internal static class PaymentsRefundOutcomeMessageFactory
{
    public static IntegrationMessageEnvelope? Create(
        PaymentRefundExecutionState state)
    {
        var resolvedAtUtc =
            state.ResolvedAtUtc;

        if (!resolvedAtUtc.HasValue)
        {
            return null;
        }

        object payload;
        string messageType;

        switch (state.Status)
        {
            case PaymentRefundStatus.Succeeded:
                var providerReference =
                    state.ProviderReference ??
                    throw new InvalidOperationException(
                        "Successful refund requires a provider reference.");

                payload =
                    new PaymentRefundedV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.ReturnRequestId,
                        state.ReturnReceiptId,
                        state.Amount,
                        state.Currency,
                        providerReference,
                        resolvedAtUtc.Value);

                messageType =
                    PaymentRefundedV1.MessageType;
                break;

            case PaymentRefundStatus.Indeterminate:
                payload =
                    new PaymentRefundIndeterminateV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.ReturnRequestId,
                        state.ReturnReceiptId,
                        state.Amount,
                        state.Currency,
                        state.ProviderReference,
                        resolvedAtUtc.Value);

                messageType =
                    PaymentRefundIndeterminateV1.MessageType;
                break;

            case PaymentRefundStatus.NotApplied:
                payload =
                    new PaymentRefundNotAppliedV1(
                        state.RequestId,
                        state.OrderId,
                        state.PaymentId,
                        state.ReturnRequestId,
                        state.ReturnReceiptId,
                        state.Amount,
                        state.Currency,
                        state.ProviderReference,
                        resolvedAtUtc.Value);

                messageType =
                    PaymentRefundNotAppliedV1.MessageType;
                break;

            default:
                return null;
        }

        return new IntegrationMessageEnvelope(
            Guid.NewGuid(),
            messageType,
            JsonSerializer.Serialize(
                payload,
                JsonSerializerOptions.Web),
            resolvedAtUtc.Value,
            state.OrderId,
            state.RequestId);
    }
}
