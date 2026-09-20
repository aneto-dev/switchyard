namespace Switchyard.Messaging;

public sealed record InboxProcessingResult(
    bool Replayed,
    DateTimeOffset ProcessedAtUtc);
