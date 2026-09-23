namespace Switchyard.Messaging.ServiceBus;

public sealed class ServiceBusMessageContractException : Exception
{
    public ServiceBusMessageContractException(string message)
        : base(message)
    {
    }

    public ServiceBusMessageContractException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
