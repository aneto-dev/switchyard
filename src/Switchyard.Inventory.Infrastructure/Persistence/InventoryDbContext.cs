using Microsoft.EntityFrameworkCore;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;

namespace Switchyard.Inventory.Infrastructure.Persistence;

public sealed class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    internal DbSet<StockItemRecord> StockItems => Set<StockItemRecord>();
    internal DbSet<ReservationRequestRecord> ReservationRequests => Set<ReservationRequestRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var stockItem = modelBuilder.Entity<StockItemRecord>();
        stockItem.ToTable(
            "stock_items",
            "inventory",
            table =>
            {
                table.HasCheckConstraint("ck_inventory_stock_on_hand", "on_hand_quantity >= 0");
                table.HasCheckConstraint("ck_inventory_stock_reserved", "reserved_quantity >= 0");
                table.HasCheckConstraint(
                    "ck_inventory_stock_reserved_not_over_on_hand",
                    "reserved_quantity <= on_hand_quantity");
                table.HasCheckConstraint("ck_inventory_stock_sku_not_blank", "btrim(sku_code) <> ''");
            });
        stockItem.HasKey(record => record.SkuCode).HasName("pk_inventory_stock_items");
        stockItem.Property(record => record.SkuCode).HasColumnName("sku_code");
        stockItem.Property(record => record.OnHandQuantity).HasColumnName("on_hand_quantity");
        stockItem.Property(record => record.ReservedQuantity).HasColumnName("reserved_quantity");

        var request = modelBuilder.Entity<ReservationRequestRecord>();
        request.ToTable(
            "reservation_requests",
            "inventory",
            table =>
            {
                table.HasCheckConstraint("ck_inventory_reservation_quantity", "quantity > 0");
                table.HasCheckConstraint("ck_inventory_reservation_outcome", "outcome IN (0, 1)");
                table.HasCheckConstraint(
                    "ck_inventory_reservation_release_reason",
                    "release_reason IS NULL OR release_reason IN (0, 1)");
                table.HasCheckConstraint(
                    "ck_inventory_reservation_release_time",
                    "released_at_utc IS NULL OR released_at_utc >= requested_at_utc");
                table.HasCheckConstraint(
                    "ck_inventory_reservation_expiry_time",
                    "expired_at_utc IS NULL OR expired_at_utc >= requested_at_utc");
                table.HasCheckConstraint(
                    "ck_inventory_reservation_shape",
                    "(outcome = 0 AND reservation_id IS NOT NULL AND expires_at_utc IS NOT NULL AND (" +
                    "(released_at_utc IS NULL AND release_reason IS NULL AND expired_at_utc IS NULL) OR " +
                    "(released_at_utc IS NOT NULL AND release_reason IS NOT NULL AND expired_at_utc IS NULL) OR " +
                    "(released_at_utc IS NULL AND release_reason IS NULL AND expired_at_utc IS NOT NULL))) OR " +
                    "(outcome = 1 AND reservation_id IS NULL AND expires_at_utc IS NULL AND " +
                    "released_at_utc IS NULL AND release_reason IS NULL AND expired_at_utc IS NULL)");
                table.HasCheckConstraint(
                    "ck_inventory_reservation_sku_not_blank",
                    "btrim(sku_code) <> ''");
            });
        request.HasKey(record => record.RequestId).HasName("pk_inventory_reservation_requests");
        request.Property(record => record.RequestId).HasColumnName("request_id");
        request.Property(record => record.OrderId).HasColumnName("order_id");
        request.Property(record => record.SkuCode).HasColumnName("sku_code").IsRequired();
        request.Property(record => record.Quantity).HasColumnName("quantity");
        request.Property(record => record.Outcome).HasColumnName("outcome").HasConversion<int>();
        request.Property(record => record.ReservationId).HasColumnName("reservation_id");
        request.Property(record => record.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        request.Property(record => record.ExpiresAtUtc).HasColumnName("expires_at_utc");
        request.Property(record => record.ReleasedAtUtc).HasColumnName("released_at_utc");
        request.Property(record => record.ReleaseReason).HasColumnName("release_reason").HasConversion<int?>();
        request.Property(record => record.ExpiredAtUtc).HasColumnName("expired_at_utc");
        request.HasIndex(record => record.ReservationId).IsUnique().HasDatabaseName("ux_inventory_reservation_id");
        request.HasIndex(record => new
        {
            record.Outcome,
            record.ReleasedAtUtc,
            record.ExpiredAtUtc,
            record.ExpiresAtUtc
        })
               .HasDatabaseName("ix_inventory_active_reservation_expiry");
    }
}
