using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Switchyard.Ordering.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations;

[DbContext(typeof(OrderingDbContext))]
partial class OrderingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

        modelBuilder
            .HasSequence<long>("order_number_sequence", "ordering")
            .StartsAt(100000L);

        modelBuilder.Entity(
            "Switchyard.Ordering.Infrastructure.Persistence.OrderRecord",
            entity =>
            {
                entity.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                entity.Property<DateTimeOffset>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at_utc");

                entity.Property<string>("OrderNumber")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("order_number");

                entity.Property<int>("Status")
                    .HasColumnType("integer")
                    .HasColumnName("status");

                entity.HasKey("Id")
                    .HasName("pk_orders");

                entity.HasIndex("OrderNumber")
                    .IsUnique()
                    .HasDatabaseName("ux_orders_order_number");

                entity.ToTable(
                    "orders",
                    "ordering",
                    table =>
                    {
                        table.HasCheckConstraint("ck_orders_order_number_not_blank", "btrim(order_number) <> ''");
                        table.HasCheckConstraint("ck_orders_status", "status IN (0, 1, 2)");
                    });
            });

        modelBuilder.Entity(
            "Switchyard.Ordering.Infrastructure.Persistence.OrderLineRecord",
            entity =>
            {
                entity.Property<Guid>("Id")
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                entity.Property<string>("Currency")
                    .IsRequired()
                    .HasMaxLength(3)
                    .HasColumnType("character varying(3)")
                    .HasColumnName("currency");

                entity.Property<Guid>("OrderId")
                    .HasColumnType("uuid")
                    .HasColumnName("order_id");

                entity.Property<int>("Position")
                    .HasColumnType("integer")
                    .HasColumnName("position");

                entity.Property<string>("ProductName")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("product_name");

                entity.Property<int>("Quantity")
                    .HasColumnType("integer")
                    .HasColumnName("quantity");

                entity.Property<string>("SkuCode")
                    .IsRequired()
                    .HasColumnType("text")
                    .HasColumnName("sku_code");

                entity.Property<decimal>("UnitPriceAmount")
                    .HasColumnType("numeric")
                    .HasColumnName("unit_price_amount");

                entity.HasKey("Id")
                    .HasName("pk_order_lines");

                entity.HasIndex("OrderId", "Position")
                    .IsUnique()
                    .HasDatabaseName("ux_order_lines_order_id_position");

                entity.ToTable(
                    "order_lines",
                    "ordering",
                    table =>
                    {
                        table.HasCheckConstraint("ck_order_lines_currency", "char_length(currency) = 3");
                        table.HasCheckConstraint("ck_order_lines_position", "position >= 0");
                        table.HasCheckConstraint("ck_order_lines_product_name_not_blank", "btrim(product_name) <> ''");
                        table.HasCheckConstraint("ck_order_lines_quantity", "quantity > 0");
                        table.HasCheckConstraint("ck_order_lines_sku_not_blank", "btrim(sku_code) <> ''");
                        table.HasCheckConstraint("ck_order_lines_unit_price", "unit_price_amount >= 0");
                    });
            });

        modelBuilder.Entity(
            "Switchyard.Ordering.Infrastructure.Persistence.OrderLineRecord",
            entity =>
            {
                entity.HasOne("Switchyard.Ordering.Infrastructure.Persistence.OrderRecord", null)
                    .WithMany("Lines")
                    .HasForeignKey("OrderId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });

        modelBuilder.Entity(
            "Switchyard.Ordering.Infrastructure.Persistence.OrderRecord",
            entity =>
            {
                entity.Navigation("Lines");
            });
#pragma warning restore 612, 618
    }
}
