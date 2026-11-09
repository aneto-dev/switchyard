using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Payments.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20261109165000_AddPaymentRefundExecution")]
public sealed class AddPaymentRefundExecution : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE payments.refund_requests
            ADD COLUMN provider_idempotency_key varchar(100) NULL,
            ADD COLUMN provider_reference varchar(100) NULL,
            ADD COLUMN provider_invocation_claimed_at_utc timestamptz NULL,
            ADD COLUMN resolved_at_utc timestamptz NULL,
            ADD COLUMN reconciliation_attempt_count integer NOT NULL DEFAULT 0,
            ADD COLUMN last_reconciled_at_utc timestamptz NULL;

            UPDATE payments.refund_requests
            SET provider_idempotency_key =
                'switchyard-refund-' ||
                replace(request_id::text, '-', '');

            ALTER TABLE payments.refund_requests
            ALTER COLUMN provider_idempotency_key SET NOT NULL;

            ALTER TABLE payments.refund_requests
            DROP CONSTRAINT ck_payments_refund_status;

            ALTER TABLE payments.refund_requests
            ADD CONSTRAINT ck_payments_refund_status
                CHECK (status IN (0, 1, 2, 3)),
            ADD CONSTRAINT ck_payments_refund_provider_key
                CHECK (btrim(provider_idempotency_key) <> ''),
            ADD CONSTRAINT ck_payments_refund_provider_reference
                CHECK (
                    provider_reference IS NULL
                    OR btrim(provider_reference) <> ''
                ),
            ADD CONSTRAINT ck_payments_refund_invocation_time
                CHECK (
                    provider_invocation_claimed_at_utc IS NULL
                    OR provider_invocation_claimed_at_utc >= requested_at_utc
                ),
            ADD CONSTRAINT ck_payments_refund_reconciliation_count
                CHECK (reconciliation_attempt_count >= 0),
            ADD CONSTRAINT ck_payments_refund_reconciliation_shape
                CHECK (
                    (reconciliation_attempt_count = 0 AND last_reconciled_at_utc IS NULL)
                    OR
                    (reconciliation_attempt_count > 0 AND last_reconciled_at_utc IS NOT NULL)
                ),
            ADD CONSTRAINT ck_payments_refund_reconciliation_time
                CHECK (
                    last_reconciled_at_utc IS NULL
                    OR last_reconciled_at_utc >= requested_at_utc
                ),
            ADD CONSTRAINT ck_payments_refund_pending_not_reconciled
                CHECK (
                    status <> 0
                    OR reconciliation_attempt_count = 0
                ),
            ADD CONSTRAINT ck_payments_refund_shape
                CHECK (
                    (status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL)
                    OR
                    (status = 1 AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL)
                    OR
                    (status IN (2, 3) AND resolved_at_utc IS NOT NULL)
                ),
            ADD CONSTRAINT ck_payments_refund_resolved_time
                CHECK (
                    resolved_at_utc IS NULL
                    OR resolved_at_utc >= requested_at_utc
                );

            CREATE UNIQUE INDEX ux_payments_refund_provider_key
                ON payments.refund_requests (provider_idempotency_key);

            CREATE UNIQUE INDEX ux_payments_refund_provider_reference
                ON payments.refund_requests (provider_reference);
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS payments.ux_payments_refund_provider_reference;
            DROP INDEX IF EXISTS payments.ux_payments_refund_provider_key;

            ALTER TABLE payments.refund_requests
            DROP CONSTRAINT IF EXISTS ck_payments_refund_resolved_time,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_shape,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_pending_not_reconciled,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_reconciliation_time,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_reconciliation_shape,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_reconciliation_count,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_invocation_time,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_provider_reference,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_provider_key,
            DROP CONSTRAINT IF EXISTS ck_payments_refund_status;

            ALTER TABLE payments.refund_requests
            ADD CONSTRAINT ck_payments_refund_status
                CHECK (status = 0);

            ALTER TABLE payments.refund_requests
            DROP COLUMN last_reconciled_at_utc,
            DROP COLUMN reconciliation_attempt_count,
            DROP COLUMN resolved_at_utc,
            DROP COLUMN provider_invocation_claimed_at_utc,
            DROP COLUMN provider_reference,
            DROP COLUMN provider_idempotency_key;
            """);
    }
}
