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
    internal DbSet<InventoryOutboxMessageRecord> OutboxMessages =>
        Set<InventoryOutboxMessageRecord>();
    internal DbSet<InventoryInboxMessageRecord> InboxMessages =>
        Set<InventoryInboxMessageRecord>();

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

        var outbox = modelBuilder.Entity<InventoryOutboxMessageRecord>();
        outbox.ToTable(
            "outbox_messages",
            "inventory",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_inventory_outbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_delivery_attempt_count",
                    "delivery_attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_attempt_shape",
                    "(delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL) OR " +
                    "(delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_lock_shape",
                    "(lock_token IS NULL AND locked_until_utc IS NULL) OR " +
                    "(lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_available_time",
                    "available_at_utc >= occurred_at_utc");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_lock_time",
                    "locked_until_utc IS NULL OR last_attempt_at_utc IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_published_time",
                    "published_at_utc IS NULL OR published_at_utc >= occurred_at_utc");
                table.HasCheckConstraint(
                    "ck_inventory_outbox_published_not_locked",
                    "published_at_utc IS NULL OR " +
                    "(lock_token IS NULL AND locked_until_utc IS NULL)");
            });
        outbox.HasKey(record => record.MessageId)
              .HasName("pk_inventory_outbox_messages");
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
              .HasColumnName("occurred_at_utc")
              .IsRequired();
        outbox.Property(record => record.CorrelationId)
              .HasColumnName("correlation_id");
        outbox.Property(record => record.CausationId)
              .HasColumnName("causation_id");
        outbox.Property(record => record.AvailableAtUtc)
              .HasColumnName("available_at_utc")
              .IsRequired();
        outbox.Property(record => record.DeliveryAttemptCount)
              .HasColumnName("delivery_attempt_count")
              .HasDefaultValue(0)
              .IsRequired();
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
        outbox.HasIndex(record => new
        {
            record.AvailableAtUtc,
            record.LockedUntilUtc,
            record.OccurredAtUtc
        })
              .HasDatabaseName("ix_inventory_outbox_due")
              .HasFilter("published_at_utc IS NULL");

        var inbox = modelBuilder.Entity<InventoryInboxMessageRecord>();
        inbox.ToTable(
            "inbox_messages",
            "inventory",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_inventory_inbox_consumer_name",
                    "btrim(consumer_name) <> ''");
                table.HasCheckConstraint(
                    "ck_inventory_inbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_inventory_inbox_payload_hash",
                    "char_length(payload_hash) = 64");
                table.HasCheckConstraint(
                    "ck_inventory_inbox_processed_time",
                    "processed_at_utc >= received_at_utc");
            });
        inbox.HasKey(record => new
        {
            record.ConsumerName,
            record.MessageId
        })
             .HasName("pk_inventory_inbox_messages");
        inbox.Property(record => record.ConsumerName)
             .HasColumnName("consumer_name")
             .HasMaxLength(200)
             .IsRequired();
        inbox.Property(record => record.MessageId)
             .HasColumnName("message_id");
        inbox.Property(record => record.MessageType)
             .HasColumnName("message_type")
             .HasMaxLength(200)
             .IsRequired();
        inbox.Property(record => record.PayloadHash)
             .HasColumnName("payload_hash")
             .HasMaxLength(64)
             .IsRequired();
        inbox.Property(record => record.OccurredAtUtc)
             .HasColumnName("occurred_at_utc")
             .IsRequired();
        inbox.Property(record => record.CorrelationId)
             .HasColumnName("correlation_id");
        inbox.Property(record => record.CausationId)
             .HasColumnName("causation_id");
        inbox.Property(record => record.ReceivedAtUtc)
             .HasColumnName("received_at_utc")
             .IsRequired();
        inbox.Property(record => record.ProcessedAtUtc)
             .HasColumnName("processed_at_utc")
             .IsRequired();
        inbox.HasIndex(record => new
        {
            record.ConsumerName,
            record.ProcessedAtUtc
        })
             .HasDatabaseName("ix_inventory_inbox_consumer_processed");
    }
}
