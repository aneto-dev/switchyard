namespace Switchyard.Messaging;

public interface IInboxMessageProcessor
{
    // The handler runs inside the consumer's local database transaction.
    // Cross-context work must be emitted through the local outbox, not performed inline.
    Task<InboxProcessingResult> ProcessAsync(
        string consumerName,
        IntegrationMessageEnvelope message,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
