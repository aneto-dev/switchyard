using Switchyard.Fulfilment.Domain.Requests;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

internal sealed class FulfilmentRequestRecord
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public FulfilmentRequestStatus Status { get; set; }
    public DateTimeOffset ConfirmedAtUtc { get; set; }
    public List<FulfilmentRequestLineRecord> Lines { get; } = [];
}

internal sealed class FulfilmentRequestLineRecord
{
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Position { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public FulfilmentRequestRecord Request { get; set; } = null!;
}

internal sealed class FulfilmentInboxMessageRecord
{
    public string ConsumerName { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
