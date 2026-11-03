using Microsoft.EntityFrameworkCore;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

public sealed class FulfilmentDbContext : DbContext
{
    public FulfilmentDbContext(
        DbContextOptions<FulfilmentDbContext> options)
        : base(options)
    {
    }

    internal DbSet<FulfilmentRequestRecord> Requests =>
        Set<FulfilmentRequestRecord>();

    internal DbSet<FulfilmentRequestLineRecord> RequestLines =>
        Set<FulfilmentRequestLineRecord>();

    internal DbSet<FulfilmentUnitRecord> Units =>
        Set<FulfilmentUnitRecord>();

    internal DbSet<FulfilmentUnitLineRecord> UnitLines =>
        Set<FulfilmentUnitLineRecord>();

    internal DbSet<FulfilmentCancellationRecord> Cancellations =>
        Set<FulfilmentCancellationRecord>();

    internal DbSet<FulfilmentCancellationLineRecord> CancellationLines =>
        Set<FulfilmentCancellationLineRecord>();

    internal DbSet<FulfilmentInboxMessageRecord> InboxMessages =>
        Set<FulfilmentInboxMessageRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var request =
            modelBuilder.Entity<FulfilmentRequestRecord>();

        request.ToTable(
            "requests",
            "fulfilment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fulfilment_request_order_number",
                    "btrim(order_number) <> ''");
                table.HasCheckConstraint(
                    "ck_fulfilment_request_status",
                    "status BETWEEN 0 AND 2");
            });

        request.HasKey(record => record.OrderId)
               .HasName("pk_fulfilment_requests");

        request.Property(record => record.OrderId)
               .HasColumnName("order_id");

        request.Property(record => record.OrderNumber)
               .HasColumnName("order_number")
               .HasMaxLength(50)
               .IsRequired();

        request.Property(record => record.Status)
               .HasColumnName("status")
               .HasConversion<int>();

        request.Property(record => record.ConfirmedAtUtc)
               .HasColumnName("confirmed_at_utc")
               .IsRequired();

        request.HasIndex(record => record.OrderNumber)
               .IsUnique()
               .HasDatabaseName("ux_fulfilment_order_number");

        var line =
            modelBuilder.Entity<FulfilmentRequestLineRecord>();

        line.ToTable(
            "request_lines",
            "fulfilment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fulfilment_line_quantity",
                    "quantity > 0");
                table.HasCheckConstraint(
                    "ck_fulfilment_line_sku",
                    "btrim(sku_code) <> ''");
                table.HasCheckConstraint(
                    "ck_fulfilment_line_product_name",
                    "btrim(product_name) <> ''");
                table.HasCheckConstraint(
                    "ck_fulfilment_line_position",
                    "position >= 0");
                table.HasCheckConstraint(
                    "ck_fulfilment_line_prepared_quantity",
                    "prepared_quantity >= 0 AND prepared_quantity <= quantity");
                table.HasCheckConstraint(
                    "ck_fulfilment_line_cancelled_quantity",
                    "cancelled_quantity >= 0 AND prepared_quantity + cancelled_quantity <= quantity");
            });

        line.HasKey(
                record => new
                {
                    record.OrderId,
                    record.OrderLineId
                })
            .HasName("pk_fulfilment_request_lines");

        line.Property(record => record.OrderId)
            .HasColumnName("order_id");

        line.Property(record => record.OrderLineId)
            .HasColumnName("order_line_id");

        line.Property(record => record.Position)
            .HasColumnName("position");

        line.Property(record => record.SkuCode)
            .HasColumnName("sku_code")
            .HasMaxLength(100)
            .IsRequired();

        line.Property(record => record.ProductName)
            .HasColumnName("product_name")
            .HasMaxLength(200)
            .IsRequired();

        line.Property(record => record.Quantity)
            .HasColumnName("quantity");

        line.Property(record => record.PreparedQuantity)
            .HasColumnName("prepared_quantity");

        line.Property(record => record.CancelledQuantity)
            .HasColumnName("cancelled_quantity");

        line.HasIndex(
                record => new
                {
                    record.OrderId,
                    record.Position
                })
            .IsUnique()
            .HasDatabaseName(
                "ux_fulfilment_request_line_position");

        request.HasMany(record => record.Lines)
               .WithOne(record => record.Request)
               .HasForeignKey(record => record.OrderId)
               .OnDelete(DeleteBehavior.Cascade);

        var unit =
            modelBuilder.Entity<FulfilmentUnitRecord>();

        unit.ToTable(
            "units",
            "fulfilment");

        unit.HasKey(
                record => new
                {
                    record.OrderId,
                    record.UnitId
                })
            .HasName(
                "pk_fulfilment_units");

        unit.Property(record => record.OrderId)
            .HasColumnName("order_id");

        unit.Property(record => record.UnitId)
            .HasColumnName("unit_id");

        unit.Property(record => record.CreatedAtUtc)
            .HasColumnName("created_at_utc");

        request.HasMany(record => record.Units)
               .WithOne(record => record.Request)
               .HasForeignKey(record => record.OrderId)
               .OnDelete(DeleteBehavior.Cascade);

        var unitLine =
            modelBuilder.Entity<FulfilmentUnitLineRecord>();

        unitLine.ToTable(
            "unit_lines",
            "fulfilment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fulfilment_unit_line_quantity",
                    "quantity > 0");
            });

        unitLine.HasKey(
                    record => new
                    {
                        record.OrderId,
                        record.UnitId,
                        record.OrderLineId
                    })
                .HasName(
                    "pk_fulfilment_unit_lines");

        unitLine.Property(record => record.OrderId)
                .HasColumnName("order_id");

        unitLine.Property(record => record.UnitId)
                .HasColumnName("unit_id");

        unitLine.Property(record => record.OrderLineId)
                .HasColumnName("order_line_id");

        unitLine.Property(record => record.Quantity)
                .HasColumnName("quantity");

        unit.HasMany(record => record.Lines)
            .WithOne(record => record.Unit)
            .HasForeignKey(
                record => new
                {
                    record.OrderId,
                    record.UnitId
                })
            .OnDelete(DeleteBehavior.Cascade);

        var cancellation =
            modelBuilder.Entity<FulfilmentCancellationRecord>();

        cancellation.ToTable(
            "cancellations",
            "fulfilment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fulfilment_cancellation_outcome",
                    "outcome IN (0, 1)");
                table.HasCheckConstraint(
                    "ck_fulfilment_cancellation_shape",
                    "(outcome = 0 AND rejection_reason IS NULL) OR " +
                    "(outcome = 1 AND rejection_reason = 0)");
            });

        cancellation.HasKey(
                    record => new
                    {
                        record.OrderId,
                        record.CancellationId
                    })
                .HasName(
                    "pk_fulfilment_cancellations");

        cancellation.Property(record => record.OrderId)
                    .HasColumnName("order_id");

        cancellation.Property(record => record.CancellationId)
                    .HasColumnName("cancellation_id");

        cancellation.Property(record => record.RequestedAtUtc)
                    .HasColumnName("requested_at_utc");

        cancellation.Property(record => record.Outcome)
                    .HasColumnName("outcome")
                    .HasConversion<int>();

        cancellation.Property(record => record.RejectionReason)
                    .HasColumnName("rejection_reason")
                    .HasConversion<int?>();

        request.HasMany(record => record.Cancellations)
               .WithOne(record => record.Request)
               .HasForeignKey(record => record.OrderId)
               .OnDelete(DeleteBehavior.Cascade);

        var cancellationLine =
            modelBuilder.Entity<FulfilmentCancellationLineRecord>();

        cancellationLine.ToTable(
            "cancellation_lines",
            "fulfilment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fulfilment_cancellation_line_quantity",
                    "quantity > 0");
            });

        cancellationLine.HasKey(
                            record => new
                            {
                                record.OrderId,
                                record.CancellationId,
                                record.OrderLineId
                            })
                        .HasName(
                            "pk_fulfilment_cancellation_lines");

        cancellationLine.Property(record => record.OrderId)
                        .HasColumnName("order_id");

        cancellationLine.Property(record => record.CancellationId)
                        .HasColumnName("cancellation_id");

        cancellationLine.Property(record => record.OrderLineId)
                        .HasColumnName("order_line_id");

        cancellationLine.Property(record => record.Quantity)
                        .HasColumnName("quantity");

        cancellation.HasMany(record => record.Lines)
                    .WithOne(record => record.Cancellation)
                    .HasForeignKey(
                        record => new
                        {
                            record.OrderId,
                            record.CancellationId
                        })
                    .OnDelete(DeleteBehavior.Cascade);

        var inbox =
            modelBuilder.Entity<FulfilmentInboxMessageRecord>();

        inbox.ToTable(
            "inbox_messages",
            "fulfilment",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fulfilment_inbox_consumer_name",
                    "btrim(consumer_name) <> ''");
                table.HasCheckConstraint(
                    "ck_fulfilment_inbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_fulfilment_inbox_payload_hash",
                    "char_length(payload_hash) = 64");
                table.HasCheckConstraint(
                    "ck_fulfilment_inbox_processed_time",
                    "processed_at_utc >= received_at_utc");
            });

        inbox.HasKey(
                record => new
                {
                    record.ConsumerName,
                    record.MessageId
                })
             .HasName(
                 "pk_fulfilment_inbox_messages");

        inbox.Property(record => record.ConsumerName)
             .HasColumnName("consumer_name")
             .HasMaxLength(200);

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
             .HasColumnName("occurred_at_utc");

        inbox.Property(record => record.CorrelationId)
             .HasColumnName("correlation_id");

        inbox.Property(record => record.CausationId)
             .HasColumnName("causation_id");

        inbox.Property(record => record.ReceivedAtUtc)
             .HasColumnName("received_at_utc");

        inbox.Property(record => record.ProcessedAtUtc)
             .HasColumnName("processed_at_utc");

        inbox.HasIndex(
                record => new
                {
                    record.ConsumerName,
                    record.ProcessedAtUtc
                })
             .HasDatabaseName(
                 "ix_fulfilment_inbox_consumer_processed");
    }
}
