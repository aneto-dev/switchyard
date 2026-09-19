namespace Switchyard.Payments.Domain.Payments;

public sealed record PaymentAmount
{
    public PaymentAmount(decimal amount, string currency)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount,
                "Payment amount must be positive.");
        }

        if (decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount,
                "Payment amount cannot contain fractions smaller than the currency minor unit.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Payment currency is required.", nameof(currency));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3 ||
            normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Payment currency must be a three-letter alphabetic code.",
                nameof(currency));
        }

        Amount = amount;
        Currency = normalizedCurrency;
    }

    public decimal Amount { get; }

    public string Currency { get; }
}
