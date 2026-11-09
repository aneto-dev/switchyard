using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Payments.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20261109153500_AddPaymentRefundRequests")]
public sealed class AddPaymentRefundRequests : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE payments.refund_requests
            (
                request_id uuid NOT NULL,
                payment_id uuid NOT NULL,
                order_id uuid NOT NULL,
                return_request_id uuid NOT NULL,
                return_receipt_id uuid NOT NULL,
                amount numeric(18, 2) NOT NULL,
                currency varchar(3) NOT NULL,
                status integer NOT NULL,
                requested_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_payments_refund_requests
                    PRIMARY KEY (request_id),
                CONSTRAINT fk_payments_refund_payment_intent
                    FOREIGN KEY (payment_id)
                    REFERENCES payments.payment_intents (payment_id)
                    ON DELETE RESTRICT,
                CONSTRAINT ck_payments_refund_amount
                    CHECK (amount > 0),
                CONSTRAINT ck_payments_refund_currency
                    CHECK (currency ~ '^[A-Z]{3}$'),
                CONSTRAINT ck_payments_refund_status
                    CHECK (status = 0)
            );

            CREATE UNIQUE INDEX ux_payments_refund_return_receipt
                ON payments.refund_requests (return_receipt_id);

            CREATE INDEX ix_payments_refund_payment
                ON payments.refund_requests (payment_id);
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS payments.refund_requests;
            """);
    }
}
