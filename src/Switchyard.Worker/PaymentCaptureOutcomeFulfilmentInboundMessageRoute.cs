using System.Text.Json;
using Switchyard.Fulfilment.Application.Payments;
using Switchyard.Fulfilment.Domain.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed class PaymentCapturedFulfilmentInboundMessageRoute :
    IFulfilmentInboundMessageRoute
{
    public string MessageType =>
        PaymentCapturedV1.MessageType;

    public Task HandleAsync(
        FulfilmentDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken) =>
        PaymentCaptureOutcomeFulfilmentInboundMessageHandler.HandleAsync(
            dbContext,
            message,
            PaymentCapturedV1.MessageType,
            payload =>
            {
                var captured =
                    JsonSerializer.Deserialize<PaymentCapturedV1>(
                        payload,
                        JsonSerializerOptions.Web) ??
                    throw new JsonException(
                        "Payment captured payload is empty.");

                return new ApplyPaymentCaptureOutcomeCommand(
                    captured.RequestId,
                    captured.OrderId,
                    captured.PaymentId,
                    FulfilmentPaymentCaptureStatus.Captured,
                    captured.ProviderReference,
                    captured.CapturedAtUtc);
            },
            cancellationToken);
}

public sealed class PaymentCaptureIndeterminateFulfilmentInboundMessageRoute :
    IFulfilmentInboundMessageRoute
{
    public string MessageType =>
        PaymentCaptureIndeterminateV1.MessageType;

    public Task HandleAsync(
        FulfilmentDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken) =>
        PaymentCaptureOutcomeFulfilmentInboundMessageHandler.HandleAsync(
            dbContext,
            message,
            PaymentCaptureIndeterminateV1.MessageType,
            payload =>
            {
                var outcome =
                    JsonSerializer.Deserialize<PaymentCaptureIndeterminateV1>(
                        payload,
                        JsonSerializerOptions.Web) ??
                    throw new JsonException(
                        "Payment capture indeterminate payload is empty.");

                return new ApplyPaymentCaptureOutcomeCommand(
                    outcome.RequestId,
                    outcome.OrderId,
                    outcome.PaymentId,
                    FulfilmentPaymentCaptureStatus.Indeterminate,
                    outcome.ProviderReference,
                    outcome.ObservedAtUtc);
            },
            cancellationToken);
}

public sealed class PaymentCaptureNotAppliedFulfilmentInboundMessageRoute :
    IFulfilmentInboundMessageRoute
{
    public string MessageType =>
        PaymentCaptureNotAppliedV1.MessageType;

    public Task HandleAsync(
        FulfilmentDbContext dbContext,
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken) =>
        PaymentCaptureOutcomeFulfilmentInboundMessageHandler.HandleAsync(
            dbContext,
            message,
            PaymentCaptureNotAppliedV1.MessageType,
            payload =>
            {
                var outcome =
                    JsonSerializer.Deserialize<PaymentCaptureNotAppliedV1>(
                        payload,
                        JsonSerializerOptions.Web) ??
                    throw new JsonException(
                        "Payment capture not-applied payload is empty.");

                return new ApplyPaymentCaptureOutcomeCommand(
                    outcome.RequestId,
                    outcome.OrderId,
                    outcome.PaymentId,
                    FulfilmentPaymentCaptureStatus.NotApplied,
                    outcome.ProviderReference,
                    outcome.ResolvedAtUtc);
            },
            cancellationToken);
}

internal static class PaymentCaptureOutcomeFulfilmentInboundMessageHandler
{
    public static async Task HandleAsync(
        FulfilmentDbContext dbContext,
        IntegrationMessageEnvelope message,
        string expectedMessageType,
        Func<string, ApplyPaymentCaptureOutcomeCommand> parse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(parse);

        ApplyPaymentCaptureOutcomeCommand command;

        try
        {
            command =
                parse(message.PayloadJson);
        }
        catch (JsonException exception)
        {
            throw new NonRetryableIntegrationMessageException(
                $"Payment capture outcome payload is invalid: {exception.Message}");
        }

        if (!string.Equals(
                message.MessageType,
                expectedMessageType,
                StringComparison.Ordinal) ||
            command.RequestId == Guid.Empty ||
            command.OrderId == Guid.Empty ||
            command.PaymentId == Guid.Empty ||
            command.ResolvedAtUtc == default ||
            message.CorrelationId != command.OrderId ||
            message.OccurredAtUtc != command.ResolvedAtUtc)
        {
            throw new NonRetryableIntegrationMessageException(
                "Payment capture outcome contains invalid identifiers, correlation or occurrence time.");
        }

        var handler =
            new ApplyPaymentCaptureOutcomeHandler(
                new EfFulfilmentRequestRepository(
                    dbContext));

        try
        {
            await handler.HandleAsync(
                command,
                cancellationToken);
        }
        catch (FulfilmentPaymentCaptureConflictException exception)
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
