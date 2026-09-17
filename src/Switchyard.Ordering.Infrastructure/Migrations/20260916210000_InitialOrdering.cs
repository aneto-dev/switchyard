using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations;

public partial class InitialOrdering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "ordering");

        migrationBuilder.CreateSequence<long>(
            name: "order_number_sequence",
            schema: "ordering",
            startValue: 100000L);

        migrationBuilder.CreateTable(
            name: "orders",
            schema: "ordering",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_number = table.Column<string>(type: "text", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_orders", x => x.id);
                table.CheckConstraint("ck_orders_order_number_not_blank", "btrim(order_number) <> ''");
                table.CheckConstraint("ck_orders_status", "status IN (0, 1, 2)");
            });

        migrationBuilder.CreateTable(
            name: "order_lines",
            schema: "ordering",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                sku_code = table.Column<string>(type: "text", nullable: false),
                product_name = table.Column<string>(type: "text", nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false),
                unit_price_amount = table.Column<decimal>(type: "numeric", nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_order_lines", x => x.id);
                table.CheckConstraint("ck_order_lines_currency", "char_length(currency) = 3");
                table.CheckConstraint("ck_order_lines_position", "position >= 0");
                table.CheckConstraint("ck_order_lines_product_name_not_blank", "btrim(product_name) <> ''");
                table.CheckConstraint("ck_order_lines_quantity", "quantity > 0");
                table.CheckConstraint("ck_order_lines_sku_not_blank", "btrim(sku_code) <> ''");
                table.CheckConstraint("ck_order_lines_unit_price", "unit_price_amount >= 0");
                table.ForeignKey(
                    name: "fk_order_lines_orders_order_id",
                    column: x => x.order_id,
                    principalSchema: "ordering",
                    principalTable: "orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_order_lines_order_id_position",
            schema: "ordering",
            table: "order_lines",
            columns: new[] { "order_id", "position" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_orders_order_number",
            schema: "ordering",
            table: "orders",
            column: "order_number",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "order_lines",
            schema: "ordering");

        migrationBuilder.DropTable(
            name: "orders",
            schema: "ordering");

        migrationBuilder.DropSequence(
            name: "order_number_sequence",
            schema: "ordering");
    }
}
