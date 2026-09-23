namespace Switchyard.Messaging;

public sealed class UnsupportedIntegrationMessageException : Exception
{
    public UnsupportedIntegrationMessageException(string messageType)
        : base($"Integration message type '{messageType}' is not supported by this consumer.")
    {
        MessageType = messageType;
    }

    public string MessageType { get; }
}
