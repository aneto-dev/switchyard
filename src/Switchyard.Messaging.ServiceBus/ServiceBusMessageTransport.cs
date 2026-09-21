using System.Globalization;
using Azure.Messaging.ServiceBus;

namespace Switchyard.Messaging.ServiceBus;

public sealed class ServiceBusMessageTransport : IMessageTransport
{
    private readonly ServiceBusSender _sender;

    public ServiceBusMessageTransport(ServiceBusSender sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public Task PublishAsync(IntegrationMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromString(message.PayloadJson))
        {
            MessageId = message.MessageId.ToString("D", CultureInfo.InvariantCulture),
            Subject = message.MessageType,
            ContentType = "application/json",
            CorrelationId = message.CorrelationId.ToString("D", CultureInfo.InvariantCulture)
        };

        serviceBusMessage.ApplicationProperties["occurredAtUtc"] =
            message.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture);

        if (message.CausationId is Guid causationId)
        {
            serviceBusMessage.ApplicationProperties["causationId"] =
                causationId.ToString("D", CultureInfo.InvariantCulture);
        }

        return _sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }
}
