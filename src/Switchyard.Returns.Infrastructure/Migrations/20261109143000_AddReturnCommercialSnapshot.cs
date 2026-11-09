using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Returns.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Returns.Infrastructure.Migrations;

[DbContext(typeof(ReturnsDbContext))]
[Migration("20261109143000_AddReturnCommercialSnapshot")]
public sealed class AddReturnCommercialSnapshot : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE returns.commercial_snapshots
            (
                order_id uuid NOT NULL,
                payment_id uuid NOT NULL,
                order_number varchar(50) NOT NULL,
                total_amount numeric NOT NULL,
                currency varchar(3) NOT NULL,
                confirmed_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_returns_commercial_snapshots
                    PRIMARY KEY (order_id),
                CONSTRAINT ck_returns_commercial_order_number
                    CHECK (btrim(order_number) <> ''),
                CONSTRAINT ck_returns_commercial_total_amount
                    CHECK (total_amount >= 0),
                CONSTRAINT ck_returns_commercial_currency
                    CHECK (char_length(currency) = 3)
            );

            CREATE UNIQUE INDEX ux_returns_commercial_payment_id
                ON returns.commercial_snapshots (payment_id);

            CREATE TABLE returns.commercial_snapshot_lines
            (
                order_id uuid NOT NULL,
                order_line_id uuid NOT NULL,
                quantity integer NOT NULL,
                unit_price_amount numeric NOT NULL,
                currency varchar(3) NOT NULL,
                CONSTRAINT pk_returns_commercial_snapshot_lines
                    PRIMARY KEY (order_id, order_line_id),
                CONSTRAINT fk_returns_commercial_lines_snapshot
                    FOREIGN KEY (order_id)
                    REFERENCES returns.commercial_snapshots (order_id)
                    ON DELETE CASCADE,
                CONSTRAINT ck_returns_commercial_line_quantity
                    CHECK (quantity > 0),
                CONSTRAINT ck_returns_commercial_line_price
                    CHECK (unit_price_amount >= 0),
                CONSTRAINT ck_returns_commercial_line_currency
                    CHECK (char_length(currency) = 3)
            );
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS returns.commercial_snapshot_lines;
            DROP TABLE IF EXISTS returns.commercial_snapshots;
            """);
    }
}
