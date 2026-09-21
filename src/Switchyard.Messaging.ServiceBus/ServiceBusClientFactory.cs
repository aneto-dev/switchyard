using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace Switchyard.Messaging.ServiceBus;

public static class ServiceBusClientFactory
{
    public static ServiceBusClient Create(ServiceBusTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ConnectionString is not null
            ? new ServiceBusClient(options.ConnectionString)
            : new ServiceBusClient(options.FullyQualifiedNamespace!, new DefaultAzureCredential());
    }
}
