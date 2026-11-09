using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Returns.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Returns.Infrastructure.Migrations;

[DbContext(typeof(ReturnsDbContext))]
[Migration("20261109110500_InitialReturnsEligibility")]
public sealed class InitialReturnsEligibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE SCHEMA IF NOT EXISTS returns;

            CREATE TABLE returns.return_eligibility
            (
                order_id uuid NOT NULL,
                order_number varchar(50) NOT NULL,
                CONSTRAINT pk_returns_return_eligibility PRIMARY KEY (order_id),
                CONSTRAINT ck_returns_order_number CHECK (btrim(order_number) <> '')
            );

            CREATE INDEX ix_returns_order_number
                ON returns.return_eligibility (order_number);

            CREATE TABLE returns.return_eligibility_lines
            (
                order_id uuid NOT NULL,
                order_line_id uuid NOT NULL,
                delivered_quantity integer NOT NULL,
                returned_quantity integer NOT NULL DEFAULT 0,
                CONSTRAINT pk_returns_return_eligibility_lines PRIMARY KEY (order_id, order_line_id),
                CONSTRAINT fk_returns_eligibility_lines_order FOREIGN KEY (order_id)
                    REFERENCES returns.return_eligibility (order_id) ON DELETE CASCADE,
                CONSTRAINT ck_returns_delivered_quantity CHECK (delivered_quantity > 0),
                CONSTRAINT ck_returns_returned_quantity CHECK (
                    returned_quantity >= 0 AND returned_quantity <= delivered_quantity)
            );

            CREATE TABLE returns.shipment_deliveries
            (
                order_id uuid NOT NULL,
                shipment_id uuid NOT NULL,
                delivered_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_returns_shipment_deliveries PRIMARY KEY (order_id, shipment_id),
                CONSTRAINT fk_returns_shipment_deliveries_order FOREIGN KEY (order_id)
                    REFERENCES returns.return_eligibility (order_id) ON DELETE CASCADE
            );

            CREATE TABLE returns.shipment_delivery_lines
            (
                order_id uuid NOT NULL,
                shipment_id uuid NOT NULL,
                order_line_id uuid NOT NULL,
                quantity integer NOT NULL,
                CONSTRAINT pk_returns_shipment_delivery_lines PRIMARY KEY (
                    order_id, shipment_id, order_line_id),
                CONSTRAINT fk_returns_shipment_delivery_lines_delivery FOREIGN KEY (
                    order_id, shipment_id)
                    REFERENCES returns.shipment_deliveries (order_id, shipment_id)
                    ON DELETE CASCADE,
                CONSTRAINT ck_returns_shipment_delivery_quantity CHECK (quantity > 0)
            );

            CREATE TABLE returns.inbox_messages
            (
                consumer_name varchar(200) NOT NULL,
                message_id uuid NOT NULL,
                message_type varchar(200) NOT NULL,
                payload_hash varchar(64) NOT NULL,
                occurred_at_utc timestamptz NOT NULL,
                correlation_id uuid NOT NULL,
                causation_id uuid NULL,
                received_at_utc timestamptz NOT NULL,
                processed_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_returns_inbox_messages PRIMARY KEY (
                    consumer_name, message_id)
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS returns.inbox_messages;
            DROP TABLE IF EXISTS returns.shipment_delivery_lines;
            DROP TABLE IF EXISTS returns.shipment_deliveries;
            DROP TABLE IF EXISTS returns.return_eligibility_lines;
            DROP TABLE IF EXISTS returns.return_eligibility;
            DROP SCHEMA IF EXISTS returns;
            """);
    }
}
