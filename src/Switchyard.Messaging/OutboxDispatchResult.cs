namespace Switchyard.Messaging;

public sealed record OutboxDispatchResult(
    int Claimed,
    int Published,
    int Failed);
