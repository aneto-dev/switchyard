using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Returns.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Returns.Infrastructure.Migrations;

[DbContext(typeof(ReturnsDbContext))]
[Migration("20261109133000_AddReturnReceiptAcceptance")]
public sealed class AddReturnReceiptAcceptance : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE returns.return_requests
            DROP CONSTRAINT ck_returns_return_request_status;

            ALTER TABLE returns.return_requests
            ADD CONSTRAINT ck_returns_return_request_status
            CHECK (status IN (0, 1, 2));

            CREATE TABLE returns.return_receipts
            (
                order_id uuid NOT NULL,
                return_receipt_id uuid NOT NULL,
                return_request_id uuid NOT NULL,
                received_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_returns_return_receipts
                    PRIMARY KEY (order_id, return_receipt_id),
                CONSTRAINT fk_returns_return_receipts_order
                    FOREIGN KEY (order_id)
                    REFERENCES returns.return_eligibility (order_id)
                    ON DELETE CASCADE,
                CONSTRAINT fk_returns_return_receipts_request
                    FOREIGN KEY (order_id, return_request_id)
                    REFERENCES returns.return_requests (
                        order_id,
                        return_request_id
                    )
                    ON DELETE CASCADE
            );

            CREATE TABLE returns.return_receipt_lines
            (
                order_id uuid NOT NULL,
                return_receipt_id uuid NOT NULL,
                order_line_id uuid NOT NULL,
                quantity integer NOT NULL,
                CONSTRAINT pk_returns_return_receipt_lines
                    PRIMARY KEY (
                        order_id,
                        return_receipt_id,
                        order_line_id
                    ),
                CONSTRAINT fk_returns_return_receipt_lines_receipt
                    FOREIGN KEY (order_id, return_receipt_id)
                    REFERENCES returns.return_receipts (
                        order_id,
                        return_receipt_id
                    )
                    ON DELETE CASCADE,
                CONSTRAINT ck_returns_return_receipt_line_quantity
                    CHECK (quantity > 0)
            );
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS returns.return_receipt_lines;
            DROP TABLE IF EXISTS returns.return_receipts;

            ALTER TABLE returns.return_requests
            DROP CONSTRAINT ck_returns_return_request_status;

            ALTER TABLE returns.return_requests
            ADD CONSTRAINT ck_returns_return_request_status
            CHECK (status = 0);
            """);
    }
}
