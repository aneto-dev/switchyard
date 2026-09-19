using System.Text.Json;

namespace Switchyard.Messaging;

public sealed record IntegrationMessageEnvelope
{
    public IntegrationMessageEnvelope(
        Guid messageId, string messageType, string payloadJson,
        DateTimeOffset occurredAtUtc, Guid correlationId, Guid? causationId)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be empty.", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Message type is required.", nameof(messageType));
        }

        var normalizedMessageType = messageType.Trim();

        if (normalizedMessageType.Length > 200)
        {
            throw new ArgumentException(
                "Message type cannot exceed 200 characters.",
                nameof(messageType));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("Message payload is required.", nameof(payloadJson));
        }

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Message payload must contain valid JSON.",
                nameof(payloadJson),
                exception);
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Correlation ID cannot be empty.",
                nameof(correlationId));
        }

        if (causationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Causation ID cannot be empty when supplied.",
                nameof(causationId));
        }

        MessageId = messageId;
        MessageType = normalizedMessageType;
        PayloadJson = payloadJson;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        CausationId = causationId;
    }

    public Guid MessageId { get; }

    public string MessageType { get; }

    public string PayloadJson { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public Guid CorrelationId { get; }

    public Guid? CausationId { get; }
}
