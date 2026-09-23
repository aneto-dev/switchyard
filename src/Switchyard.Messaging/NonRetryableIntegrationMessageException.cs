namespace Switchyard.Messaging;

public sealed class NonRetryableIntegrationMessageException : Exception
{
    public NonRetryableIntegrationMessageException(string message)
        : base(message)
    {
    }
}
