using Microsoft.EntityFrameworkCore;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class ReturnsDbContext : DbContext
{
    public ReturnsDbContext(DbContextOptions<ReturnsDbContext> options)
        : base(options)
    {
    }

    internal DbSet<ReturnEligibilityRecord> Eligibilities =>
        Set<ReturnEligibilityRecord>();

    internal DbSet<ReturnEligibilityLineRecord> EligibilityLines =>
        Set<ReturnEligibilityLineRecord>();

    internal DbSet<ShipmentDeliveryRecord> ShipmentDeliveries =>
        Set<ShipmentDeliveryRecord>();

    internal DbSet<ShipmentDeliveryLineRecord> ShipmentDeliveryLines =>
        Set<ShipmentDeliveryLineRecord>();

    internal DbSet<ReturnsInboxMessageRecord> InboxMessages =>
        Set<ReturnsInboxMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var eligibility = modelBuilder.Entity<ReturnEligibilityRecord>();

        eligibility.ToTable(
            "return_eligibility",
            "returns",
            table =>
                table.HasCheckConstraint(
                    "ck_returns_order_number",
                    "btrim(order_number) <> ''"));

        eligibility.HasKey(record => record.OrderId)
                   .HasName("pk_returns_return_eligibility");

        eligibility.Property(record => record.OrderId)
                   .HasColumnName("order_id");

        eligibility.Property(record => record.OrderNumber)
                   .HasColumnName("order_number")
                   .HasMaxLength(50)
                   .IsRequired();

        eligibility.HasIndex(record => record.OrderNumber)
                   .HasDatabaseName("ix_returns_order_number");

        var line = modelBuilder.Entity<ReturnEligibilityLineRecord>();

        line.ToTable(
            "return_eligibility_lines",
            "returns",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_returns_delivered_quantity",
                    "delivered_quantity > 0");
                table.HasCheckConstraint(
                    "ck_returns_returned_quantity",
                    "returned_quantity >= 0 AND returned_quantity <= delivered_quantity");
            });

        line.HasKey(record => new { record.OrderId, record.OrderLineId })
            .HasName("pk_returns_return_eligibility_lines");

        line.Property(record => record.OrderId)
            .HasColumnName("order_id");

        line.Property(record => record.OrderLineId)
            .HasColumnName("order_line_id");

        line.Property(record => record.DeliveredQuantity)
            .HasColumnName("delivered_quantity");

        line.Property(record => record.ReturnedQuantity)
            .HasColumnName("returned_quantity");

        eligibility.HasMany(record => record.Lines)
                   .WithOne(record => record.Eligibility)
                   .HasForeignKey(record => record.OrderId)
                   .OnDelete(DeleteBehavior.Cascade);

        var delivery = modelBuilder.Entity<ShipmentDeliveryRecord>();

        delivery.ToTable("shipment_deliveries", "returns");

        delivery.HasKey(record => new { record.OrderId, record.ShipmentId })
                .HasName("pk_returns_shipment_deliveries");

        delivery.Property(record => record.OrderId)
                .HasColumnName("order_id");

        delivery.Property(record => record.ShipmentId)
                .HasColumnName("shipment_id");

        delivery.Property(record => record.DeliveredAtUtc)
                .HasColumnName("delivered_at_utc");

        eligibility.HasMany(record => record.Deliveries)
                   .WithOne(record => record.Eligibility)
                   .HasForeignKey(record => record.OrderId)
                   .OnDelete(DeleteBehavior.Cascade);

        var deliveryLine = modelBuilder.Entity<ShipmentDeliveryLineRecord>();

        deliveryLine.ToTable(
            "shipment_delivery_lines",
            "returns",
            table =>
                table.HasCheckConstraint(
                    "ck_returns_shipment_delivery_quantity",
                    "quantity > 0"));

        deliveryLine.HasKey(
                    record => new
                    {
                        record.OrderId,
                        record.ShipmentId,
                        record.OrderLineId
                    })
                    .HasName("pk_returns_shipment_delivery_lines");

        deliveryLine.Property(record => record.OrderId)
                    .HasColumnName("order_id");

        deliveryLine.Property(record => record.ShipmentId)
                    .HasColumnName("shipment_id");

        deliveryLine.Property(record => record.OrderLineId)
                    .HasColumnName("order_line_id");

        deliveryLine.Property(record => record.Quantity)
                    .HasColumnName("quantity");

        delivery.HasMany(record => record.Lines)
                .WithOne(record => record.Delivery)
                .HasForeignKey(record => new { record.OrderId, record.ShipmentId })
                .OnDelete(DeleteBehavior.Cascade);

        var inbox = modelBuilder.Entity<ReturnsInboxMessageRecord>();

        inbox.ToTable("inbox_messages", "returns");

        inbox.HasKey(record => new { record.ConsumerName, record.MessageId })
             .HasName("pk_returns_inbox_messages");

        inbox.Property(record => record.ConsumerName)
             .HasColumnName("consumer_name")
             .HasMaxLength(200);

        inbox.Property(record => record.MessageId)
             .HasColumnName("message_id");

        inbox.Property(record => record.MessageType)
             .HasColumnName("message_type")
             .HasMaxLength(200);

        inbox.Property(record => record.PayloadHash)
             .HasColumnName("payload_hash")
             .HasMaxLength(64);

        inbox.Property(record => record.OccurredAtUtc)
             .HasColumnName("occurred_at_utc");

        inbox.Property(record => record.CorrelationId)
             .HasColumnName("correlation_id");

        inbox.Property(record => record.CausationId)
             .HasColumnName("causation_id");

        inbox.Property(record => record.ReceivedAtUtc)
             .HasColumnName("received_at_utc");

        inbox.Property(record => record.ProcessedAtUtc)
             .HasColumnName("processed_at_utc");
    }
}
