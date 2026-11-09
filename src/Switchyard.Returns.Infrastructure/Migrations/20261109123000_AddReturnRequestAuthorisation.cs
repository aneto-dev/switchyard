using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Returns.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Returns.Infrastructure.Migrations;

[DbContext(typeof(ReturnsDbContext))]
[Migration("20261109123000_AddReturnRequestAuthorisation")]
public sealed class AddReturnRequestAuthorisation : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE returns.return_eligibility_lines
            ADD COLUMN authorised_return_quantity integer NOT NULL DEFAULT 0;

            ALTER TABLE returns.return_eligibility_lines
            ADD CONSTRAINT ck_returns_authorised_quantity
            CHECK (
                authorised_return_quantity >= 0
                AND authorised_return_quantity + returned_quantity <= delivered_quantity
            );

            CREATE TABLE returns.return_requests
            (
                order_id uuid NOT NULL,
                return_request_id uuid NOT NULL,
                requested_at_utc timestamptz NOT NULL,
                status integer NOT NULL,
                CONSTRAINT pk_returns_return_requests
                    PRIMARY KEY (order_id, return_request_id),
                CONSTRAINT fk_returns_return_requests_order
                    FOREIGN KEY (order_id)
                    REFERENCES returns.return_eligibility (order_id)
                    ON DELETE CASCADE,
                CONSTRAINT ck_returns_return_request_status
                    CHECK (status = 0)
            );

            CREATE TABLE returns.return_request_lines
            (
                order_id uuid NOT NULL,
                return_request_id uuid NOT NULL,
                order_line_id uuid NOT NULL,
                quantity integer NOT NULL,
                CONSTRAINT pk_returns_return_request_lines
                    PRIMARY KEY (
                        order_id,
                        return_request_id,
                        order_line_id
                    ),
                CONSTRAINT fk_returns_return_request_lines_request
                    FOREIGN KEY (order_id, return_request_id)
                    REFERENCES returns.return_requests (
                        order_id,
                        return_request_id
                    )
                    ON DELETE CASCADE,
                CONSTRAINT ck_returns_return_request_line_quantity
                    CHECK (quantity > 0)
            );
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS returns.return_request_lines;
            DROP TABLE IF EXISTS returns.return_requests;

            ALTER TABLE returns.return_eligibility_lines
            DROP CONSTRAINT IF EXISTS ck_returns_authorised_quantity;

            ALTER TABLE returns.return_eligibility_lines
            DROP COLUMN IF EXISTS authorised_return_quantity;
            """);
    }
}
