using Microsoft.EntityFrameworkCore;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
    {
    }

    internal DbSet<OrderRecord> Orders => Set<OrderRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder
            .HasSequence<long>("order_number_sequence", "ordering")
            .StartsAt(100000L);

        var order = modelBuilder.Entity<OrderRecord>();
        order.ToTable(
            "orders",
            "ordering",
            table =>
            {
                table.HasCheckConstraint("ck_orders_order_number_not_blank", "btrim(order_number) <> ''");
                table.HasCheckConstraint("ck_orders_status", "status IN (0, 1, 2)");
            });
        order.HasKey(record => record.Id).HasName("pk_orders");
        order.Property(record => record.Id).HasColumnName("id");
        order.Property(record => record.OrderNumber).HasColumnName("order_number").IsRequired();
        order.Property(record => record.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        order.Property(record => record.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        order.HasIndex(record => record.OrderNumber).IsUnique().HasDatabaseName("ux_orders_order_number");

        var line = modelBuilder.Entity<OrderLineRecord>();
        line.ToTable(
            "order_lines",
            "ordering",
            table =>
            {
                table.HasCheckConstraint("ck_order_lines_position", "position >= 0");
                table.HasCheckConstraint("ck_order_lines_sku_not_blank", "btrim(sku_code) <> ''");
                table.HasCheckConstraint("ck_order_lines_product_name_not_blank", "btrim(product_name) <> ''");
                table.HasCheckConstraint("ck_order_lines_quantity", "quantity > 0");
                table.HasCheckConstraint("ck_order_lines_unit_price", "unit_price_amount >= 0");
                table.HasCheckConstraint("ck_order_lines_currency", "char_length(currency) = 3");
            });
        line.HasKey(record => record.Id).HasName("pk_order_lines");
        line.Property(record => record.Id).HasColumnName("id");
        line.Property(record => record.OrderId).HasColumnName("order_id");
        line.Property(record => record.Position).HasColumnName("position");
        line.Property(record => record.SkuCode).HasColumnName("sku_code").IsRequired();
        line.Property(record => record.ProductName).HasColumnName("product_name").IsRequired();
        line.Property(record => record.Quantity).HasColumnName("quantity");
        line.Property(record => record.UnitPriceAmount).HasColumnName("unit_price_amount").HasColumnType("numeric");
        line.Property(record => record.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        line.HasIndex(record => new { record.OrderId, record.Position })
            .IsUnique()
            .HasDatabaseName("ux_order_lines_order_id_position");

        order.HasMany(record => record.Lines)
            .WithOne()
            .HasForeignKey(record => record.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
