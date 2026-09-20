using Microsoft.EntityFrameworkCore;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
    {
    }

    internal DbSet<OrderRecord> Orders => Set<OrderRecord>();

    internal DbSet<OrderRequestRecord> OrderRequests => Set<OrderRequestRecord>();

    internal DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    internal DbSet<InboxMessageRecord> InboxMessages => Set<InboxMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>("order_number_sequence", "ordering")
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

        var orderRequest = modelBuilder.Entity<OrderRequestRecord>();
        orderRequest.ToTable(
            "order_requests",
            "ordering",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_order_requests_idempotency_key_not_blank",
                    "btrim(idempotency_key) <> ''");
                table.HasCheckConstraint(
                    "ck_order_requests_request_fingerprint",
                    "char_length(request_fingerprint) = 64");
            });
        orderRequest.HasKey(record => record.IdempotencyKey).HasName("pk_order_requests");
        orderRequest.Property(record => record.IdempotencyKey)
                    .HasColumnName("idempotency_key")
                    .HasMaxLength(128);
        orderRequest.Property(record => record.RequestFingerprint)
                    .HasColumnName("request_fingerprint")
                    .HasMaxLength(64)
                    .IsRequired();
        orderRequest.Property(record => record.OrderId).HasColumnName("order_id");
        orderRequest.Property(record => record.AcceptedAtUtc)
                    .HasColumnName("accepted_at_utc")
                    .IsRequired();
        orderRequest.HasIndex(record => record.OrderId)
                    .IsUnique()
                    .HasDatabaseName("ux_order_requests_order_id");
        orderRequest.HasOne<OrderRecord>()
                    .WithOne()
                    .HasForeignKey<OrderRequestRecord>(record => record.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);

        var outbox = modelBuilder.Entity<OutboxMessageRecord>();
        outbox.ToTable(
            "outbox_messages",
            "ordering",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ordering_outbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_delivery_attempt_count",
                    "delivery_attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_attempt_shape",
                    "(delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL) OR " +
                    "(delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_lock_shape",
                    "(lock_token IS NULL AND locked_until_utc IS NULL) OR " +
                    "(lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_available_time",
                    "available_at_utc >= occurred_at_utc");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_lock_time",
                    "locked_until_utc IS NULL OR last_attempt_at_utc IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_published_time",
                    "published_at_utc IS NULL OR published_at_utc >= occurred_at_utc");
                table.HasCheckConstraint(
                    "ck_ordering_outbox_published_not_locked",
                    "published_at_utc IS NULL OR (lock_token IS NULL AND locked_until_utc IS NULL)");
            });
        outbox.HasKey(record => record.MessageId)
              .HasName("pk_ordering_outbox_messages");
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
              .HasDatabaseName("ix_ordering_outbox_due")
              .HasFilter("published_at_utc IS NULL");

        var inbox = modelBuilder.Entity<InboxMessageRecord>();
        inbox.ToTable(
            "inbox_messages",
            "ordering",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ordering_inbox_consumer_name",
                    "btrim(consumer_name) <> ''");
                table.HasCheckConstraint(
                    "ck_ordering_inbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_ordering_inbox_payload_hash",
                    "char_length(payload_hash) = 64");
                table.HasCheckConstraint(
                    "ck_ordering_inbox_processed_time",
                    "processed_at_utc >= received_at_utc");
            });
        inbox.HasKey(record => new
        {
            record.ConsumerName,
            record.MessageId
        })
             .HasName("pk_ordering_inbox_messages");
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
             .HasDatabaseName("ix_ordering_inbox_consumer_processed");
    }
}
