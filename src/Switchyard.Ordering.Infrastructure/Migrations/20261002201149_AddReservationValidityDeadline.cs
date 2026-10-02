using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationValidityDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reservation_expires_at_utc",
                schema: "ordering",
                table: "order_placement_lines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_placement_lines_reservation_expiry_shape",
                schema: "ordering",
                table: "order_placement_lines",
                sql: "reservation_expires_at_utc IS NULL OR reservation_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_placement_lines_reservation_expiry_shape",
                schema: "ordering",
                table: "order_placement_lines");

            migrationBuilder.DropColumn(
                name: "reservation_expires_at_utc",
                schema: "ordering",
                table: "order_placement_lines");
        }
    }
}
