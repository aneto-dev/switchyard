namespace Switchyard.Returns.Application.Refunds;

public sealed class ReturnRefundNotFoundException : Exception
{
    public ReturnRefundNotFoundException(
        Guid orderId,
        Guid returnReceiptId)
        : base(
            $"Refund projection for return receipt '{returnReceiptId}' on order '{orderId}' does not exist.")
    {
    }
}

public sealed class ReturnRefundConflictException : Exception
{
    public ReturnRefundConflictException(
        Guid orderId,
        Guid returnReceiptId)
        : base(
            $"Refund projection for return receipt '{returnReceiptId}' on order '{orderId}' conflicts with the incoming outcome.")
    {
    }
}
