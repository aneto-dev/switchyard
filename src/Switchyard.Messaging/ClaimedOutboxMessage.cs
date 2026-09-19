namespace Switchyard.Messaging;

public sealed record ClaimedOutboxMessage(
    IntegrationMessageEnvelope Message,
    Guid LockToken,
    int DeliveryAttemptCount);
