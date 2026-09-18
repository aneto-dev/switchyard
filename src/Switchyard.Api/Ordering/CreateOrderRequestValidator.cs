namespace Switchyard.Api.Ordering;

internal static class CreateOrderRequestValidator
{
    private const int MaxIdempotencyKeyLength = 128;

    public static Dictionary<string, string[]> Validate(
        CreateOrderRequest request, string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        ValidateIdempotencyKey(errors, idempotencyKey);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            AddError(errors, "lines", "At least one order line is required.");
            return ToValidationProblem(errors);
        }

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var prefix = $"lines[{index}]";

            if (line is null)
            {
                AddError(errors, prefix, "Order line is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.SkuCode))
            {
                AddError(errors, $"{prefix}.skuCode", "SKU code is required.");
            }

            if (string.IsNullOrWhiteSpace(line.ProductName))
            {
                AddError(errors, $"{prefix}.productName", "Product name is required.");
            }

            if (line.Quantity <= 0)
            {
                AddError(errors, $"{prefix}.quantity", "Quantity must be positive.");
            }

            if (line.UnitPriceAmount < 0m)
            {
                AddError(errors, $"{prefix}.unitPriceAmount", "Unit price cannot be negative.");
            }

            if (string.IsNullOrWhiteSpace(line.Currency))
            {
                AddError(errors, $"{prefix}.currency", "Currency is required.");
            }
            else if (line.Currency.Trim().Length != 3)
            {
                AddError(errors, $"{prefix}.currency", "Currency must be a three-letter code.");
            }
        }

        var currencies = request.Lines.Where(line => line is not null &&
                                                     !string.IsNullOrWhiteSpace(line.Currency) &&
                                                     line.Currency.Trim().Length == 3)
                                      .Select(line => line!.Currency!.Trim().ToUpperInvariant())
                                      .Distinct(StringComparer.Ordinal)
                                      .Take(2)
                                      .Count();

        if (currencies > 1)
        {
            AddError(errors, "lines", "All order lines must use the same currency.");
        }

        return ToValidationProblem(errors);
    }

    private static void ValidateIdempotencyKey(
        Dictionary<string, List<string>> errors, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            AddError(errors, "idempotencyKey", "Exactly one Idempotency-Key header is required.");
            return;
        }

        if (idempotencyKey.Trim().Length > MaxIdempotencyKeyLength)
        {
            AddError(
                errors,
                "idempotencyKey",
                $"Idempotency-Key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static Dictionary<string, string[]> ToValidationProblem(
        Dictionary<string, List<string>> errors)
    {
        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
