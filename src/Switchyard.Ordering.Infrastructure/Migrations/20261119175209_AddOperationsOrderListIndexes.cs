using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsOrderListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at_utc_id",
                schema: "ordering",
                table: "orders",
                columns: new[] { "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_created_at_utc_id",
                schema: "ordering",
                table: "orders",
                columns: new[] { "status", "created_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_created_at_utc_id",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_status_created_at_utc_id",
                schema: "ordering",
                table: "orders");
        }
    }
}
