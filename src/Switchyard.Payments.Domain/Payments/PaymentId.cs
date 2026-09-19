namespace Switchyard.Payments.Domain.Payments;

public sealed record PaymentId
{
    public PaymentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Payment ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PaymentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
