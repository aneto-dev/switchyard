namespace Switchyard.Ordering.Application.Orders;

public sealed class DuplicateOrderRequestException : Exception
{
    public DuplicateOrderRequestException(Exception innerException)
        : base("The order request idempotency key already exists.", innerException)
    {
    }
}
