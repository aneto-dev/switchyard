namespace Switchyard.Inventory.Infrastructure.Persistence;

internal sealed class StockItemRecord
{
    public string SkuCode { get; set; } = string.Empty;

    public int OnHandQuantity { get; set; }

    public int ReservedQuantity { get; set; }
}
