namespace Switchyard.Ordering.Domain.Orders;

public sealed record Money
{
    public Money(decimal amount, string currency)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3)
        {
            throw new ArgumentException("Currency must be a three-letter code.", nameof(currency));
        }

        Amount = amount;
        Currency = normalizedCurrency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Gbp(decimal amount) => new(amount, "GBP");

    public Money Multiply(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        return new Money(Amount * quantity, Currency);
    }

    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Money values with different currencies cannot be added.");
        }

        return new Money(left.Amount + right.Amount, left.Currency);
    }
}
