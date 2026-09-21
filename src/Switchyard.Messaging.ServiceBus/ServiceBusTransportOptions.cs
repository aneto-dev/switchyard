namespace Switchyard.Messaging.ServiceBus;

public sealed record ServiceBusTransportOptions
{
    public ServiceBusTransportOptions(string topicName, string? connectionString, string? fullyQualifiedNamespace)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new ArgumentException("Service Bus topic name is required.", nameof(topicName));
        }

        TopicName = topicName.Trim();
        ConnectionString = NormalizeOptional(connectionString);
        FullyQualifiedNamespace = NormalizeOptional(fullyQualifiedNamespace);

        if ((ConnectionString is null) == (FullyQualifiedNamespace is null))
        {
            throw new ArgumentException(
                "Configure exactly one Service Bus credential source: a local connection string or a fully qualified namespace for Microsoft Entra authentication.");
        }
    }

    public string TopicName { get; }
    public string? ConnectionString { get; }
    public string? FullyQualifiedNamespace { get; }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
