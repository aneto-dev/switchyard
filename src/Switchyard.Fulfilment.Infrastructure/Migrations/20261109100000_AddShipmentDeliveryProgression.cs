using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261109100000_AddShipmentDeliveryProgression")]
public partial class AddShipmentDeliveryProgression : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "delivered_at_utc",
            schema: "fulfilment",
            table: "shipments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_shipment_delivery_shape",
            schema: "fulfilment",
            table: "shipments",
            sql: "delivered_at_utc IS NULL OR dispatched_at_utc IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_shipment_delivery_time",
            schema: "fulfilment",
            table: "shipments",
            sql: "delivered_at_utc IS NULL OR delivered_at_utc >= dispatched_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_shipment_delivery_time",
            schema: "fulfilment",
            table: "shipments");

        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_shipment_delivery_shape",
            schema: "fulfilment",
            table: "shipments");

        migrationBuilder.DropColumn(
            name: "delivered_at_utc",
            schema: "fulfilment",
            table: "shipments");
    }
}
