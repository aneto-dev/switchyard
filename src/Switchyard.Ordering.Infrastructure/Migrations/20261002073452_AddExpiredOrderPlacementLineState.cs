using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiredOrderPlacementLineState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_placement_lines_reservation_shape",
                schema: "ordering",
                table: "order_placement_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_placement_lines_state",
                schema: "ordering",
                table: "order_placement_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_placement_lines_reservation_shape",
                schema: "ordering",
                table: "order_placement_lines",
                sql: "(state IN (0, 2) AND reservation_id IS NULL) OR (state IN (1, 3, 4, 5) AND reservation_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_placement_lines_state",
                schema: "ordering",
                table: "order_placement_lines",
                sql: "state BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_placement_lines_reservation_shape",
                schema: "ordering",
                table: "order_placement_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_placement_lines_state",
                schema: "ordering",
                table: "order_placement_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_placement_lines_reservation_shape",
                schema: "ordering",
                table: "order_placement_lines",
                sql: "(state IN (0, 2) AND reservation_id IS NULL) OR (state IN (1, 3, 4) AND reservation_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_placement_lines_state",
                schema: "ordering",
                table: "order_placement_lines",
                sql: "state BETWEEN 0 AND 4");
        }
    }
}
