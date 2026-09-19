namespace Switchyard.Messaging;

public sealed class OutboxLeaseLostException : Exception
{
    public OutboxLeaseLostException(Guid messageId)
        : base($"Outbox lease for message '{messageId}' is no longer owned by this dispatcher.")
    {
        MessageId = messageId;
    }

    public Guid MessageId { get; }
}
