using Microsoft.EntityFrameworkCore;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class ReturnsDbContext : DbContext
{
    public ReturnsDbContext(DbContextOptions<ReturnsDbContext> options)
        : base(options)
    {
    }

    internal DbSet<ReturnCommercialSnapshotRecord> CommercialSnapshots =>
        Set<ReturnCommercialSnapshotRecord>();

    internal DbSet<ReturnCommercialSnapshotLineRecord> CommercialSnapshotLines =>
        Set<ReturnCommercialSnapshotLineRecord>();

    internal DbSet<ReturnEligibilityRecord> Eligibilities =>
        Set<ReturnEligibilityRecord>();

    internal DbSet<ReturnEligibilityLineRecord> EligibilityLines =>
        Set<ReturnEligibilityLineRecord>();

    internal DbSet<ShipmentDeliveryRecord> ShipmentDeliveries =>
        Set<ShipmentDeliveryRecord>();

    internal DbSet<ShipmentDeliveryLineRecord> ShipmentDeliveryLines =>
        Set<ShipmentDeliveryLineRecord>();

    internal DbSet<ReturnRequestRecord> ReturnRequests =>
        Set<ReturnRequestRecord>();

    internal DbSet<ReturnRequestLineRecord> ReturnRequestLines =>
        Set<ReturnRequestLineRecord>();

    internal DbSet<ReturnReceiptRecord> ReturnReceipts =>
        Set<ReturnReceiptRecord>();

    internal DbSet<ReturnReceiptLineRecord> ReturnReceiptLines =>
        Set<ReturnReceiptLineRecord>();

    internal DbSet<ReturnsOutboxMessageRecord> OutboxMessages =>
        Set<ReturnsOutboxMessageRecord>();

    internal DbSet<ReturnsInboxMessageRecord> InboxMessages =>
        Set<ReturnsInboxMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var commercial =
            modelBuilder.Entity<ReturnCommercialSnapshotRecord>();

        commercial.ToTable(
            "commercial_snapshots",
            "returns",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_returns_commercial_order_number",
                    "btrim(order_number) <> ''");
                table.HasCheckConstraint(
                    "ck_returns_commercial_total_amount",
                    "total_amount >= 0");
                table.HasCheckConstraint(
                    "ck_returns_commercial_currency",
                    "char_length(currency) = 3");
            });

        commercial.HasKey(record => record.OrderId)
                  .HasName("pk_returns_commercial_snapshots");

        commercial.Property(record => record.OrderId)
                  .HasColumnName("order_id");

        commercial.Property(record => record.PaymentId)
                  .HasColumnName("payment_id");

        commercial.Property(record => record.OrderNumber)
                  .HasColumnName("order_number")
                  .HasMaxLength(50)
                  .IsRequired();

        commercial.Property(record => record.TotalAmount)
                  .HasColumnName("total_amount")
                  .HasColumnType("numeric");

        commercial.Property(record => record.Currency)
                  .HasColumnName("currency")
                  .HasMaxLength(3)
                  .IsRequired();

        commercial.Property(record => record.ConfirmedAtUtc)
                  .HasColumnName("confirmed_at_utc");

        commercial.HasIndex(record => record.PaymentId)
                  .IsUnique()
                  .HasDatabaseName("ux_returns_commercial_payment_id");

        var commercialLine =
            modelBuilder.Entity<ReturnCommercialSnapshotLineRecord>();

        commercialLine.ToTable(
            "commercial_snapshot_lines",
            "returns",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_returns_commercial_line_quantity",
                    "quantity > 0");
                table.HasCheckConstraint(
                    "ck_returns_commercial_line_price",
                    "unit_price_amount >= 0");
                table.HasCheckConstraint(
                    "ck_returns_commercial_line_currency",
                    "char_length(currency) = 3");
            });

        commercialLine.HasKey(
                          record => new
                          {
                              record.OrderId,
                              record.OrderLineId
                          })
                      .HasName(
                          "pk_returns_commercial_snapshot_lines");

        commercialLine.Property(record => record.OrderId)
                      .HasColumnName("order_id");

        commercialLine.Property(record => record.OrderLineId)
                      .HasColumnName("order_line_id");

        commercialLine.Property(record => record.Quantity)
                      .HasColumnName("quantity");

        commercialLine.Property(record => record.UnitPriceAmount)
                      .HasColumnName("unit_price_amount")
                      .HasColumnType("numeric");

        commercialLine.Property(record => record.Currency)
                      .HasColumnName("currency")
                      .HasMaxLength(3)
                      .IsRequired();

        commercial.HasMany(record => record.Lines)
                  .WithOne(record => record.Snapshot)
                  .HasForeignKey(record => record.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);

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

        line.HasKey(record => new { record.OrderId, record.OrderLineId })
            .HasName("pk_returns_return_eligibility_lines");

        line.Property(record => record.OrderId)
            .HasColumnName("order_id");

        line.Property(record => record.OrderLineId)
            .HasColumnName("order_line_id");

        line.Property(record => record.DeliveredQuantity)
            .HasColumnName("delivered_quantity");

        line.Property(record => record.AuthorisedReturnQuantity)
            .HasColumnName("authorised_return_quantity");

        line.Property(record => record.ReturnedQuantity)
            .HasColumnName("returned_quantity");

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
                table.HasCheckConstraint(
                    "ck_returns_authorised_quantity",
                    "authorised_return_quantity >= 0 AND authorised_return_quantity + returned_quantity <= delivered_quantity");
            });

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

        var returnRequest =
            modelBuilder.Entity<ReturnRequestRecord>();

        returnRequest.ToTable(
            "return_requests",
            "returns",
            table =>
                table.HasCheckConstraint(
                    "ck_returns_return_request_status",
                    "status IN (0, 1, 2)"));

        returnRequest.HasKey(
                         record => new
                         {
                             record.OrderId,
                             record.ReturnRequestId
                         })
                     .HasName(
                         "pk_returns_return_requests");

        returnRequest.Property(record => record.OrderId)
                     .HasColumnName("order_id");

        returnRequest.Property(record => record.ReturnRequestId)
                     .HasColumnName("return_request_id");

        returnRequest.Property(record => record.RequestedAtUtc)
                     .HasColumnName("requested_at_utc");

        returnRequest.Property(record => record.Status)
                     .HasColumnName("status");

        eligibility.HasMany(record => record.ReturnRequests)
                   .WithOne(record => record.Eligibility)
                   .HasForeignKey(record => record.OrderId)
                   .OnDelete(DeleteBehavior.Cascade);

        var returnRequestLine =
            modelBuilder.Entity<ReturnRequestLineRecord>();

        returnRequestLine.ToTable(
            "return_request_lines",
            "returns",
            table =>
                table.HasCheckConstraint(
                    "ck_returns_return_request_line_quantity",
                    "quantity > 0"));

        returnRequestLine.HasKey(
                             record => new
                             {
                                 record.OrderId,
                                 record.ReturnRequestId,
                                 record.OrderLineId
                             })
                         .HasName(
                             "pk_returns_return_request_lines");

        returnRequestLine.Property(record => record.OrderId)
                         .HasColumnName("order_id");

        returnRequestLine.Property(record => record.ReturnRequestId)
                         .HasColumnName("return_request_id");

        returnRequestLine.Property(record => record.OrderLineId)
                         .HasColumnName("order_line_id");

        returnRequestLine.Property(record => record.Quantity)
                         .HasColumnName("quantity");

        returnRequest.HasMany(record => record.Lines)
                     .WithOne(record => record.ReturnRequest)
                     .HasForeignKey(
                         record => new
                         {
                             record.OrderId,
                             record.ReturnRequestId
                         })
                     .OnDelete(DeleteBehavior.Cascade);

        var returnReceipt =
            modelBuilder.Entity<ReturnReceiptRecord>();

        returnReceipt.ToTable(
            "return_receipts",
            "returns");

        returnReceipt.HasKey(
                         record => new
                         {
                             record.OrderId,
                             record.ReturnReceiptId
                         })
                     .HasName(
                         "pk_returns_return_receipts");

        returnReceipt.Property(record => record.OrderId)
                     .HasColumnName("order_id");

        returnReceipt.Property(record => record.ReturnReceiptId)
                     .HasColumnName("return_receipt_id");

        returnReceipt.Property(record => record.ReturnRequestId)
                     .HasColumnName("return_request_id");

        returnReceipt.Property(record => record.ReceivedAtUtc)
                     .HasColumnName("received_at_utc");

        eligibility.HasMany(record => record.ReturnReceipts)
                   .WithOne(record => record.Eligibility)
                   .HasForeignKey(record => record.OrderId)
                   .OnDelete(DeleteBehavior.Cascade);

        returnReceipt.HasOne(record => record.ReturnRequest)
                     .WithMany()
                     .HasForeignKey(
                         record => new
                         {
                             record.OrderId,
                             record.ReturnRequestId
                         })
                     .OnDelete(DeleteBehavior.Cascade);

        var returnReceiptLine =
            modelBuilder.Entity<ReturnReceiptLineRecord>();

        returnReceiptLine.ToTable(
            "return_receipt_lines",
            "returns",
            table =>
                table.HasCheckConstraint(
                    "ck_returns_return_receipt_line_quantity",
                    "quantity > 0"));

        returnReceiptLine.HasKey(
                             record => new
                             {
                                 record.OrderId,
                                 record.ReturnReceiptId,
                                 record.OrderLineId
                             })
                         .HasName(
                             "pk_returns_return_receipt_lines");

        returnReceiptLine.Property(record => record.OrderId)
                         .HasColumnName("order_id");

        returnReceiptLine.Property(record => record.ReturnReceiptId)
                         .HasColumnName("return_receipt_id");

        returnReceiptLine.Property(record => record.OrderLineId)
                         .HasColumnName("order_line_id");

        returnReceiptLine.Property(record => record.Quantity)
                         .HasColumnName("quantity");

        returnReceipt.HasMany(record => record.Lines)
                     .WithOne(record => record.ReturnReceipt)
                     .HasForeignKey(
                         record => new
                         {
                             record.OrderId,
                             record.ReturnReceiptId
                         })
                     .OnDelete(DeleteBehavior.Cascade);

        var outbox =
            modelBuilder.Entity<ReturnsOutboxMessageRecord>();

        outbox.ToTable(
            "outbox_messages",
            "returns",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_returns_outbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_returns_outbox_delivery_attempt_count",
                    "delivery_attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_returns_outbox_attempt_shape",
                    "(delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL) OR " +
                    "(delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_returns_outbox_lock_shape",
                    "(lock_token IS NULL AND locked_until_utc IS NULL) OR " +
                    "(lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_returns_outbox_published_time",
                    "published_at_utc IS NULL OR published_at_utc >= occurred_at_utc");
            });

        outbox.HasKey(record => record.MessageId)
              .HasName("pk_returns_outbox_messages");

        outbox.Property(record => record.MessageId)
              .HasColumnName("message_id");

        outbox.Property(record => record.MessageType)
              .HasColumnName("message_type")
              .HasMaxLength(200)
              .IsRequired();

        outbox.Property(record => record.PayloadJson)
              .HasColumnName("payload")
              .HasColumnType("jsonb")
              .IsRequired();

        outbox.Property(record => record.OccurredAtUtc)
              .HasColumnName("occurred_at_utc");

        outbox.Property(record => record.CorrelationId)
              .HasColumnName("correlation_id");

        outbox.Property(record => record.CausationId)
              .HasColumnName("causation_id");

        outbox.Property(record => record.AvailableAtUtc)
              .HasColumnName("available_at_utc");

        outbox.Property(record => record.DeliveryAttemptCount)
              .HasColumnName("delivery_attempt_count");

        outbox.Property(record => record.LastAttemptAtUtc)
              .HasColumnName("last_attempt_at_utc");

        outbox.Property(record => record.LockToken)
              .HasColumnName("lock_token");

        outbox.Property(record => record.LockedUntilUtc)
              .HasColumnName("locked_until_utc");

        outbox.Property(record => record.PublishedAtUtc)
              .HasColumnName("published_at_utc");

        outbox.Property(record => record.LastError)
              .HasColumnName("last_error")
              .HasMaxLength(200);

        outbox.HasIndex(
                record => new
                {
                    record.AvailableAtUtc,
                    record.LockedUntilUtc,
                    record.OccurredAtUtc
                })
              .HasDatabaseName(
                  "ix_returns_outbox_due")
              .HasFilter(
                  "published_at_utc IS NULL");

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
