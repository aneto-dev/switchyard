using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReservationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_reservation_shape",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expired_at_utc",
                schema: "inventory",
                table: "reservation_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "release_reason",
                schema: "inventory",
                table: "reservation_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "released_at_utc",
                schema: "inventory",
                table: "reservation_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_active_reservation_expiry",
                schema: "inventory",
                table: "reservation_requests",
                columns: new[] { "outcome", "released_at_utc", "expired_at_utc", "expires_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_reservation_expiry_time",
                schema: "inventory",
                table: "reservation_requests",
                sql: "expired_at_utc IS NULL OR expired_at_utc >= requested_at_utc");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_reservation_release_reason",
                schema: "inventory",
                table: "reservation_requests",
                sql: "release_reason IS NULL OR release_reason IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_reservation_release_time",
                schema: "inventory",
                table: "reservation_requests",
                sql: "released_at_utc IS NULL OR released_at_utc >= requested_at_utc");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_reservation_shape",
                schema: "inventory",
                table: "reservation_requests",
                sql: "(outcome = 0 AND reservation_id IS NOT NULL AND expires_at_utc IS NOT NULL AND ((released_at_utc IS NULL AND release_reason IS NULL AND expired_at_utc IS NULL) OR (released_at_utc IS NOT NULL AND release_reason IS NOT NULL AND expired_at_utc IS NULL) OR (released_at_utc IS NULL AND release_reason IS NULL AND expired_at_utc IS NOT NULL))) OR (outcome = 1 AND reservation_id IS NULL AND expires_at_utc IS NULL AND released_at_utc IS NULL AND release_reason IS NULL AND expired_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_active_reservation_expiry",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_reservation_expiry_time",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_reservation_release_reason",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_reservation_release_time",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_reservation_shape",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropColumn(
                name: "expired_at_utc",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropColumn(
                name: "release_reason",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.DropColumn(
                name: "released_at_utc",
                schema: "inventory",
                table: "reservation_requests");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_reservation_shape",
                schema: "inventory",
                table: "reservation_requests",
                sql: "(outcome = 0 AND reservation_id IS NOT NULL AND expires_at_utc IS NOT NULL) OR (outcome = 1 AND reservation_id IS NULL AND expires_at_utc IS NULL)");
        }
    }
}
