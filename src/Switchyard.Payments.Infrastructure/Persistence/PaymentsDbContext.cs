using Microsoft.EntityFrameworkCore;
using Switchyard.Payments.Domain.Authorisation;

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
    }
}
