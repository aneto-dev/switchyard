using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPlacementProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_placement_processes",
                schema: "ordering",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_placement_processes", x => x.order_id);
                    table.CheckConstraint("ck_order_placement_process_state", "state BETWEEN 0 AND 8");
                    table.CheckConstraint("ck_order_placement_process_times", "updated_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "FK_order_placement_processes_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_placement_lines",
                schema: "ordering",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_code = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_placement_lines", x => new { x.order_id, x.order_line_id });
                    table.CheckConstraint("ck_order_placement_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_order_placement_lines_reservation_shape", "(state IN (0, 2) AND reservation_id IS NULL) OR (state IN (1, 3, 4) AND reservation_id IS NOT NULL)");
                    table.CheckConstraint("ck_order_placement_lines_sku", "btrim(sku_code) <> ''");
                    table.CheckConstraint("ck_order_placement_lines_state", "state BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_order_placement_lines_order_placement_processes_order_id",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "order_placement_processes",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_order_placement_lines_reservation_request_id",
                schema: "ordering",
                table: "order_placement_lines",
                column: "reservation_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_placement_lines",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "order_placement_processes",
                schema: "ordering");
        }
    }
}
