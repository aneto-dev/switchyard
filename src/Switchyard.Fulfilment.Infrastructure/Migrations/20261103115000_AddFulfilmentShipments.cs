using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261103115000_AddFulfilmentShipments")]
public partial class AddFulfilmentShipments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "payment_capture_request_id",
            schema: "fulfilment",
            table: "requests",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "payment_capture_requested_at_utc",
            schema: "fulfilment",
            table: "requests",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "shipments",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fulfilment_shipments", x => new { x.order_id, x.shipment_id });
                table.CheckConstraint("ck_fulfilment_shipment_dispatch_time", "dispatched_at_utc IS NULL OR dispatched_at_utc >= created_at_utc");
                table.ForeignKey(
                    name: "fk_fulfilment_shipments_requests_order_id",
                    column: x => x.order_id,
                    principalSchema: "fulfilment",
                    principalTable: "requests",
                    principalColumn: "order_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "shipment_units",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                unit_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fulfilment_shipment_units", x => new { x.order_id, x.shipment_id, x.unit_id });
                table.ForeignKey(
                    name: "fk_fulfilment_shipment_units_shipments_order_id_shipment_id",
                    columns: x => new { x.order_id, x.shipment_id },
                    principalSchema: "fulfilment",
                    principalTable: "shipments",
                    principalColumns: new[] { "order_id", "shipment_id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_fulfilment_shipment_unit",
            schema: "fulfilment",
            table: "shipment_units",
            columns: new[] { "order_id", "unit_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "shipment_units", schema: "fulfilment");
        migrationBuilder.DropTable(name: "shipments", schema: "fulfilment");
        migrationBuilder.DropColumn(name: "payment_capture_request_id", schema: "fulfilment", table: "requests");
        migrationBuilder.DropColumn(name: "payment_capture_requested_at_utc", schema: "fulfilment", table: "requests");
    }
}
