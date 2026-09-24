using Azure.Messaging.ServiceBus;
using Switchyard.Messaging.ServiceBus;

namespace Switchyard.Worker;

public sealed class ServiceBusSubscriptionEndpoint :
    IAsyncDisposable
{
    public ServiceBusSubscriptionEndpoint(
        string name,
        ServiceBusProcessor processor,
        ServiceBusInboundMessageProcessor messageProcessor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Service Bus endpoint name is required.",
                nameof(name));
        }

        Name = name.Trim();

        Processor =
            processor ??
            throw new ArgumentNullException(nameof(processor));

        MessageProcessor =
            messageProcessor ??
            throw new ArgumentNullException(nameof(messageProcessor));
    }

    public string Name { get; }

    public ServiceBusProcessor Processor { get; }

    public ServiceBusInboundMessageProcessor MessageProcessor { get; }

    public ValueTask DisposeAsync() =>
        Processor.DisposeAsync();
}
