using Switchyard.Inventory.Domain.Reservations;
using Switchyard.Inventory.Domain.Stock;
using Xunit;

namespace Switchyard.Inventory.Domain.Tests;

public sealed class InventoryDomainTests
{
    [Fact]
    public void StockItemTracksAvailableQuantityAndRejectsOverReservation()
    {
        var stock = new StockItem(new InventorySku(" BIKE-001 "), 3, 1);

        Assert.Equal("BIKE-001", stock.Sku.Value);
        Assert.Equal(2, stock.AvailableQuantity);

        var reserved = stock.Reserve(2);

        Assert.Equal(3, reserved.ReservedQuantity);
        Assert.Equal(0, reserved.AvailableQuantity);

        Assert.Throws<InvalidOperationException>(() => reserved.Reserve(1));
    }

    [Fact]
    public void StockReservationRequiresExpiryAfterReservationTime()
    {
        var reservedAt = new DateTimeOffset(2026, 9, 18, 9, 30, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StockReservation(
                StockReservationId.New(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new InventorySku("BIKE-001"),
                1,
                reservedAt,
                reservedAt));
    }
}
