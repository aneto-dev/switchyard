using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "reservation_requests",
                schema: "inventory",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_code = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservation_requests", x => x.request_id);
                    table.CheckConstraint("ck_inventory_reservation_outcome", "outcome IN (0, 1)");
                    table.CheckConstraint("ck_inventory_reservation_quantity", "quantity > 0");
                    table.CheckConstraint("ck_inventory_reservation_shape", "(outcome = 0 AND reservation_id IS NOT NULL AND expires_at_utc IS NOT NULL) OR (outcome = 1 AND reservation_id IS NULL AND expires_at_utc IS NULL)");
                    table.CheckConstraint("ck_inventory_reservation_sku_not_blank", "btrim(sku_code) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "inventory",
                columns: table => new
                {
                    sku_code = table.Column<string>(type: "text", nullable: false),
                    on_hand_quantity = table.Column<int>(type: "integer", nullable: false),
                    reserved_quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_stock_items", x => x.sku_code);
                    table.CheckConstraint("ck_inventory_stock_on_hand", "on_hand_quantity >= 0");
                    table.CheckConstraint("ck_inventory_stock_reserved", "reserved_quantity >= 0");
                    table.CheckConstraint("ck_inventory_stock_reserved_not_over_on_hand", "reserved_quantity <= on_hand_quantity");
                    table.CheckConstraint("ck_inventory_stock_sku_not_blank", "btrim(sku_code) <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "ux_inventory_reservation_id",
                schema: "inventory",
                table: "reservation_requests",
                column: "reservation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservation_requests",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_items",
                schema: "inventory");
        }
    }
}
