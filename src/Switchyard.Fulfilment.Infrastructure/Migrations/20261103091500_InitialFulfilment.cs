using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261103091500_InitialFulfilment")]
public partial class InitialFulfilment : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "fulfilment");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "fulfilment",
            columns: table => new
            {
                consumer_name = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false),
                message_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                message_type = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false),
                payload_hash = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                correlation_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                causation_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                received_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                processed_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_fulfilment_inbox_messages",
                    x => new
                    {
                        x.consumer_name,
                        x.message_id
                    });
                table.CheckConstraint(
                    "ck_fulfilment_inbox_consumer_name",
                    "btrim(consumer_name) <> ''");
                table.CheckConstraint(
                    "ck_fulfilment_inbox_message_type",
                    "btrim(message_type) <> ''");
                table.CheckConstraint(
                    "ck_fulfilment_inbox_payload_hash",
                    "char_length(payload_hash) = 64");
                table.CheckConstraint(
                    "ck_fulfilment_inbox_processed_time",
                    "processed_at_utc >= received_at_utc");
            });

        migrationBuilder.CreateTable(
            name: "requests",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                order_number = table.Column<string>(
                    type: "character varying(50)",
                    maxLength: 50,
                    nullable: false),
                status = table.Column<int>(
                    type: "integer",
                    nullable: false),
                confirmed_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_fulfilment_requests",
                    x => x.order_id);
                table.CheckConstraint(
                    "ck_fulfilment_request_order_number",
                    "btrim(order_number) <> ''");
                table.CheckConstraint(
                    "ck_fulfilment_request_status",
                    "status = 0");
            });

        migrationBuilder.CreateTable(
            name: "request_lines",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                order_line_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                position = table.Column<int>(
                    type: "integer",
                    nullable: false),
                sku_code = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false),
                product_name = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false),
                quantity = table.Column<int>(
                    type: "integer",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_fulfilment_request_lines",
                    x => new
                    {
                        x.order_id,
                        x.order_line_id
                    });
                table.CheckConstraint(
                    "ck_fulfilment_line_position",
                    "position >= 0");
                table.CheckConstraint(
                    "ck_fulfilment_line_product_name",
                    "btrim(product_name) <> ''");
                table.CheckConstraint(
                    "ck_fulfilment_line_quantity",
                    "quantity > 0");
                table.CheckConstraint(
                    "ck_fulfilment_line_sku",
                    "btrim(sku_code) <> ''");
                table.ForeignKey(
                    name: "fk_fulfilment_request_lines_requests_order_id",
                    column: x => x.order_id,
                    principalSchema: "fulfilment",
                    principalTable: "requests",
                    principalColumn: "order_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_fulfilment_inbox_consumer_processed",
            schema: "fulfilment",
            table: "inbox_messages",
            columns: new[]
            {
                "consumer_name",
                "processed_at_utc"
            });

        migrationBuilder.CreateIndex(
            name: "ux_fulfilment_order_number",
            schema: "fulfilment",
            table: "requests",
            column: "order_number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_fulfilment_request_line_position",
            schema: "fulfilment",
            table: "request_lines",
            columns: new[]
            {
                "order_id",
                "position"
            },
            unique: true);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "fulfilment");

        migrationBuilder.DropTable(
            name: "request_lines",
            schema: "fulfilment");

        migrationBuilder.DropTable(
            name: "requests",
            schema: "fulfilment");
    }
}
