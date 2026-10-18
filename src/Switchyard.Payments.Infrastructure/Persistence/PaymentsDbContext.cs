using Microsoft.EntityFrameworkCore;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Settlement;

namespace Switchyard.Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
        : base(options)
    {
    }

    internal DbSet<PaymentIntentRecord> PaymentIntents => Set<PaymentIntentRecord>();

    internal DbSet<PaymentAuthorisationAttemptRecord> AuthorisationAttempts =>
        Set<PaymentAuthorisationAttemptRecord>();

    internal DbSet<PaymentSettlementAttemptRecord> SettlementAttempts =>
        Set<PaymentSettlementAttemptRecord>();

    internal DbSet<PaymentOutboxMessageRecord> OutboxMessages =>
        Set<PaymentOutboxMessageRecord>();

    internal DbSet<PaymentInboxMessageRecord> InboxMessages =>
        Set<PaymentInboxMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var intent = modelBuilder.Entity<PaymentIntentRecord>();
        intent.ToTable(
            "payment_intents",
            "payments",
            table =>
            {
                table.HasCheckConstraint("ck_payments_intent_amount", "amount > 0");
                table.HasCheckConstraint(
                    "ck_payments_intent_currency",
                    "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint(
                    "ck_payments_intent_authorisation_status",
                    "authorisation_status IN (0, 1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_payments_intent_authorisation_shape",
                    "(authorisation_status = 0 AND authorisation_resolved_at_utc IS NULL) OR " +
                    "(authorisation_status IN (1, 2, 3) AND authorisation_resolved_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_intent_authorisation_time",
                    "authorisation_resolved_at_utc IS NULL OR authorisation_resolved_at_utc >= created_at_utc");
            });
        intent.HasKey(record => record.PaymentId).HasName("pk_payments_payment_intents");
        intent.Property(record => record.PaymentId).HasColumnName("payment_id");
        intent.Property(record => record.OrderId).HasColumnName("order_id");
        intent.Property(record => record.Amount).HasColumnName("amount").HasPrecision(18, 2);
        intent.Property(record => record.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        intent.Property(record => record.AuthorisationStatus)
              .HasColumnName("authorisation_status")
              .HasConversion<int>();
        intent.Property(record => record.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        intent.Property(record => record.AuthorisationResolvedAtUtc)
              .HasColumnName("authorisation_resolved_at_utc");
        intent.HasIndex(record => record.OrderId)
              .IsUnique()
              .HasDatabaseName("ux_payments_payment_intent_order");

        var attempt = modelBuilder.Entity<PaymentAuthorisationAttemptRecord>();
        attempt.ToTable(
            "authorisation_attempts",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_authorisation_status",
                    "status IN (0, 1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_provider_key",
                    "btrim(provider_idempotency_key) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_provider_reference",
                    "provider_reference IS NULL OR btrim(provider_reference) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_shape",
                    "(status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL) OR " +
                    "(status IN (1, 2) AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL) OR " +
                    "(status = 3 AND resolved_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_time",
                    "resolved_at_utc IS NULL OR resolved_at_utc >= requested_at_utc");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_reconciliation_count",
                    "reconciliation_attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_reconciliation_shape",
                    "(reconciliation_attempt_count = 0 AND last_reconciled_at_utc IS NULL) OR " +
                    "(reconciliation_attempt_count > 0 AND last_reconciled_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_pending_not_reconciled",
                    "status <> 0 OR reconciliation_attempt_count = 0");
                table.HasCheckConstraint(
                    "ck_payments_authorisation_reconciliation_time",
                    "last_reconciled_at_utc IS NULL OR last_reconciled_at_utc >= requested_at_utc");
            });
        attempt.HasKey(record => record.RequestId).HasName("pk_payments_authorisation_attempts");
        attempt.Property(record => record.RequestId).HasColumnName("request_id");
        attempt.Property(record => record.PaymentId).HasColumnName("payment_id");
        attempt.Property(record => record.ProviderIdempotencyKey)
               .HasColumnName("provider_idempotency_key")
               .HasMaxLength(100)
               .IsRequired();
        attempt.Property(record => record.ProviderReference)
               .HasColumnName("provider_reference")
               .HasMaxLength(100);
        attempt.Property(record => record.Status).HasColumnName("status").HasConversion<int>();
        attempt.Property(record => record.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        attempt.Property(record => record.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        attempt.Property(record => record.ReconciliationAttemptCount)
               .HasColumnName("reconciliation_attempt_count")
               .HasDefaultValue(0)
               .IsRequired();
        attempt.Property(record => record.LastReconciledAtUtc)
               .HasColumnName("last_reconciled_at_utc");
        attempt.HasIndex(record => record.PaymentId)
               .IsUnique()
               .HasDatabaseName("ux_payments_authorisation_payment");
        attempt.HasIndex(record => record.ProviderIdempotencyKey)
               .IsUnique()
               .HasDatabaseName("ux_payments_authorisation_provider_key");
        attempt.HasIndex(record => record.ProviderReference)
               .IsUnique()
               .HasDatabaseName("ux_payments_authorisation_provider_reference");
        attempt.HasOne<PaymentIntentRecord>()
               .WithMany()
               .HasForeignKey(record => record.PaymentId)
               .OnDelete(DeleteBehavior.Restrict)
               .HasConstraintName("fk_payments_authorisation_payment_intent");

        var settlement = modelBuilder.Entity<PaymentSettlementAttemptRecord>();
        settlement.ToTable(
            "settlement_attempts",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_settlement_action",
                    "action IN (0, 1)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_status",
                    "status IN (0, 1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_provider_key",
                    "btrim(provider_idempotency_key) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_settlement_provider_reference",
                    "provider_reference IS NULL OR btrim(provider_reference) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_settlement_shape",
                    "(status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL) OR " +
                    "(status = 1 AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL) OR " +
                    "(status IN (2, 3) AND resolved_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_time",
                    "resolved_at_utc IS NULL OR resolved_at_utc >= requested_at_utc");
                table.HasCheckConstraint(
                    "ck_payments_settlement_reconciliation_count",
                    "reconciliation_attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_payments_settlement_reconciliation_shape",
                    "(reconciliation_attempt_count = 0 AND last_reconciled_at_utc IS NULL) OR " +
                    "(reconciliation_attempt_count > 0 AND last_reconciled_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_settlement_pending_not_reconciled",
                    "status <> 0 OR reconciliation_attempt_count = 0");
                table.HasCheckConstraint(
                    "ck_payments_settlement_reconciliation_time",
                    "last_reconciled_at_utc IS NULL OR last_reconciled_at_utc >= requested_at_utc");
            });
        settlement.HasKey(record => record.RequestId)
                  .HasName("pk_payments_settlement_attempts");
        settlement.Property(record => record.RequestId).HasColumnName("request_id");
        settlement.Property(record => record.PaymentId).HasColumnName("payment_id");
        settlement.Property(record => record.Action).HasColumnName("action").HasConversion<int>();
        settlement.Property(record => record.Status).HasColumnName("status").HasConversion<int>();
        settlement.Property(record => record.ProviderIdempotencyKey)
                  .HasColumnName("provider_idempotency_key")
                  .HasMaxLength(100)
                  .IsRequired();
        settlement.Property(record => record.ProviderReference)
                  .HasColumnName("provider_reference")
                  .HasMaxLength(100);
        settlement.Property(record => record.RequestedAtUtc)
                  .HasColumnName("requested_at_utc")
                  .IsRequired();
        settlement.Property(record => record.ResolvedAtUtc)
                  .HasColumnName("resolved_at_utc");
        settlement.Property(record => record.ReconciliationAttemptCount)
                  .HasColumnName("reconciliation_attempt_count")
                  .HasDefaultValue(0)
                  .IsRequired();
        settlement.Property(record => record.LastReconciledAtUtc)
                  .HasColumnName("last_reconciled_at_utc");
        settlement.HasIndex(record => record.PaymentId)
                  .IsUnique()
                  .HasDatabaseName("ux_payments_settlement_payment");
        settlement.HasIndex(record => record.ProviderIdempotencyKey)
                  .IsUnique()
                  .HasDatabaseName("ux_payments_settlement_provider_key");
        settlement.HasIndex(record => record.ProviderReference)
                  .IsUnique()
                  .HasDatabaseName("ux_payments_settlement_provider_reference");
        settlement.HasOne<PaymentIntentRecord>()
                  .WithMany()
                  .HasForeignKey(record => record.PaymentId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("fk_payments_settlement_payment_intent");

        var outbox = modelBuilder.Entity<PaymentOutboxMessageRecord>();
        outbox.ToTable(
            "outbox_messages",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_outbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_outbox_delivery_attempt_count",
                    "delivery_attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_payments_outbox_attempt_shape",
                    "(delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL) OR " +
                    "(delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_outbox_lock_shape",
                    "(lock_token IS NULL AND locked_until_utc IS NULL) OR " +
                    "(lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_outbox_available_time",
                    "available_at_utc >= occurred_at_utc");
                table.HasCheckConstraint(
                    "ck_payments_outbox_lock_time",
                    "locked_until_utc IS NULL OR last_attempt_at_utc IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_payments_outbox_published_time",
                    "published_at_utc IS NULL OR published_at_utc >= occurred_at_utc");
                table.HasCheckConstraint(
                    "ck_payments_outbox_published_not_locked",
                    "published_at_utc IS NULL OR " +
                    "(lock_token IS NULL AND locked_until_utc IS NULL)");
            });
        outbox.HasKey(record => record.MessageId)
              .HasName("pk_payments_outbox_messages");
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
              .HasDatabaseName("ix_payments_outbox_due")
              .HasFilter("published_at_utc IS NULL");

        var inbox = modelBuilder.Entity<PaymentInboxMessageRecord>();
        inbox.ToTable(
            "inbox_messages",
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_inbox_consumer_name",
                    "btrim(consumer_name) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_inbox_message_type",
                    "btrim(message_type) <> ''");
                table.HasCheckConstraint(
                    "ck_payments_inbox_payload_hash",
                    "char_length(payload_hash) = 64");
                table.HasCheckConstraint(
                    "ck_payments_inbox_processed_time",
                    "processed_at_utc IS NULL OR processed_at_utc >= received_at_utc");
                table.HasCheckConstraint(
                    "ck_payments_inbox_processing_lock_shape",
                    "(processing_token IS NULL AND processing_locked_until_utc IS NULL) OR " +
                    "(processing_token IS NOT NULL AND processing_locked_until_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_payments_inbox_processing_lock_time",
                    "processing_locked_until_utc IS NULL OR " +
                    "processing_locked_until_utc >= received_at_utc");
                table.HasCheckConstraint(
                    "ck_payments_inbox_processed_not_locked",
                    "processed_at_utc IS NULL OR " +
                    "(processing_token IS NULL AND processing_locked_until_utc IS NULL)");
            });
        inbox.HasKey(record => new
        {
            record.ConsumerName,
            record.MessageId
        })
             .HasName("pk_payments_inbox_messages");
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
             .HasColumnName("processed_at_utc");
        inbox.Property(record => record.ProcessingToken)
             .HasColumnName("processing_token");
        inbox.Property(record => record.ProcessingLockedUntilUtc)
             .HasColumnName("processing_locked_until_utc");
        inbox.HasIndex(record => new
        {
            record.ConsumerName,
            record.ProcessedAtUtc
        })
             .HasDatabaseName("ix_payments_inbox_consumer_processed");
    }
}
