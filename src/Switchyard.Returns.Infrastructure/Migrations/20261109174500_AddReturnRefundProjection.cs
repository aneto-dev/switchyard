using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Returns.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Returns.Infrastructure.Migrations;

[DbContext(typeof(ReturnsDbContext))]
[Migration("20261109174500_AddReturnRefundProjection")]
public sealed class AddReturnRefundProjection : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE returns.return_refunds
            (
                order_id uuid NOT NULL,
                return_receipt_id uuid NOT NULL,
                return_request_id uuid NOT NULL,
                payment_id uuid NOT NULL,
                amount numeric(18, 2) NOT NULL,
                currency varchar(3) NOT NULL,
                status integer NOT NULL,
                provider_reference varchar(100) NULL,
                requested_at_utc timestamptz NOT NULL,
                updated_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_returns_return_refunds
                    PRIMARY KEY (order_id, return_receipt_id),
                CONSTRAINT fk_returns_refund_receipt
                    FOREIGN KEY (order_id, return_receipt_id)
                    REFERENCES returns.return_receipts (
                        order_id,
                        return_receipt_id
                    )
                    ON DELETE CASCADE,
                CONSTRAINT ck_returns_refund_amount
                    CHECK (amount > 0),
                CONSTRAINT ck_returns_refund_currency
                    CHECK (currency ~ '^[A-Z]{3}$'),
                CONSTRAINT ck_returns_refund_status
                    CHECK (status IN (0, 1, 2, 3)),
                CONSTRAINT ck_returns_refund_provider_reference
                    CHECK (
                        provider_reference IS NULL
                        OR btrim(provider_reference) <> ''
                    ),
                CONSTRAINT ck_returns_refund_updated_time
                    CHECK (updated_at_utc >= requested_at_utc)
            );

            CREATE INDEX ix_returns_refund_payment
                ON returns.return_refunds (payment_id);

            INSERT INTO returns.return_refunds
            (
                order_id,
                return_receipt_id,
                return_request_id,
                payment_id,
                amount,
                currency,
                status,
                provider_reference,
                requested_at_utc,
                updated_at_utc
            )
            SELECT
                correlation_id,
                (payload ->> 'returnReceiptId')::uuid,
                (payload ->> 'returnRequestId')::uuid,
                (payload ->> 'paymentId')::uuid,
                (payload ->> 'amount')::numeric(18, 2),
                upper(payload ->> 'currency'),
                0,
                NULL,
                occurred_at_utc,
                occurred_at_utc
            FROM returns.outbox_messages
            WHERE message_type = 'payments.command.refund.v1'
            ON CONFLICT (order_id, return_receipt_id) DO NOTHING;
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS returns.return_refunds;
            """);
    }
}
