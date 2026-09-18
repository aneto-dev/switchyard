namespace Switchyard.Ordering.Application.Orders;

public sealed class OrderRequestConflictException : Exception
{
    public OrderRequestConflictException(string idempotencyKey)
        : base("The idempotency key has already been used with a different request.")
    {
        IdempotencyKey = idempotencyKey;
    }

    public string IdempotencyKey { get; }
}
