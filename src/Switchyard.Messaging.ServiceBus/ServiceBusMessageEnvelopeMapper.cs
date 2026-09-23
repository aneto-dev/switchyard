using System.Globalization;
using Azure.Messaging.ServiceBus;

namespace Switchyard.Messaging.ServiceBus;

public static class ServiceBusMessageEnvelopeMapper
{
    private const string OccurredAtUtcProperty = "occurredAtUtc";
    private const string CausationIdProperty = "causationId";

    public static IntegrationMessageEnvelope Map(
        ServiceBusReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageId = ParseRequiredGuid(
            message.MessageId,
            "MessageId");

        var correlationId = ParseRequiredGuid(
            message.CorrelationId,
            "CorrelationId");

        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            throw new ServiceBusMessageContractException(
                "Service Bus Subject is required.");
        }

        if (!string.Equals(
                message.ContentType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ServiceBusMessageContractException(
                "Service Bus ContentType must be application/json.");
        }

        var occurredAtUtc = ParseOccurredAtUtc(message);
        var causationId = ParseOptionalCausationId(message);

        try
        {
            return new IntegrationMessageEnvelope(
                messageId,
                message.Subject,
                message.Body.ToString(),
                occurredAtUtc,
                correlationId,
                causationId);
        }
        catch (ArgumentException exception)
        {
            throw new ServiceBusMessageContractException(
                "Service Bus message contains invalid integration-envelope data.",
                exception);
        }
    }

    private static DateTimeOffset ParseOccurredAtUtc(
        ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(
                OccurredAtUtcProperty,
                out var value) ||
            value is not string rawValue ||
            !DateTimeOffset.TryParseExact(
                rawValue,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var occurredAtUtc))
        {
            throw new ServiceBusMessageContractException(
                $"Service Bus application property '{OccurredAtUtcProperty}' must contain a round-trip timestamp.");
        }

        return occurredAtUtc;
    }

    private static Guid? ParseOptionalCausationId(
        ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(
                CausationIdProperty,
                out var value))
        {
            return null;
        }

        if (value is not string rawValue ||
            !Guid.TryParse(rawValue, out var causationId) ||
            causationId == Guid.Empty)
        {
            throw new ServiceBusMessageContractException(
                $"Service Bus application property '{CausationIdProperty}' must contain a non-empty GUID.");
        }

        return causationId;
    }

    private static Guid ParseRequiredGuid(
        string? value,
        string fieldName)
    {
        if (!Guid.TryParse(value, out var parsed) ||
            parsed == Guid.Empty)
        {
            throw new ServiceBusMessageContractException(
                $"Service Bus {fieldName} must contain a non-empty GUID.");
        }

        return parsed;
    }
}
