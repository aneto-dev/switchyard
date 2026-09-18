using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Application.Orders;

internal static class OrderRequestFingerprint
{
    public static string Calculate(IReadOnlyCollection<CreatePendingOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        var builder = new StringBuilder();
        string? currency = null;

        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);

            var sku = new SkuCode(line.SkuCode);
            var product = new ProductSnapshot(sku, line.ProductName);
            var money = new Money(line.UnitPriceAmount, line.Currency);

            if (line.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lines),
                    line.Quantity,
                    "Quantity must be positive.");
            }

            if (currency is not null &&
                !string.Equals(currency, money.Currency, StringComparison.Ordinal))
            {
                throw new ArgumentException("All order lines must use the same currency.", nameof(lines));
            }

            currency = money.Currency;

            Append(builder, product.SkuCode.Value);
            Append(builder, product.ProductName);
            Append(builder, line.Quantity.ToString(CultureInfo.InvariantCulture));
            Append(builder, money.Amount.ToString("G29", CultureInfo.InvariantCulture));
            Append(builder, money.Currency);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length)
               .Append(':')
               .Append(value)
               .Append('|');
    }
}
