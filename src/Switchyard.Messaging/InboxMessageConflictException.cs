namespace Switchyard.Messaging;

public sealed class InboxMessageConflictException : Exception
{
    public InboxMessageConflictException(string consumerName, Guid messageId)
        : base(
            $"Inbox message '{messageId}' for consumer '{consumerName}' " +
            "was previously processed with different message identity.")
    {
        ConsumerName = consumerName;
        MessageId = messageId;
    }

    public string ConsumerName { get; }

    public Guid MessageId { get; }
}
