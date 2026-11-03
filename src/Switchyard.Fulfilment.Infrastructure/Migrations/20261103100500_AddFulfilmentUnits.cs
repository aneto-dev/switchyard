using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

public partial class AddFulfilmentUnits : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_request_status",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.AddColumn<int>(
            name: "prepared_quantity",
            schema: "fulfilment",
            table: "request_lines",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_request_status",
            schema: "fulfilment",
            table: "requests",
            sql: "status BETWEEN 0 AND 2");

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_line_prepared_quantity",
            schema: "fulfilment",
            table: "request_lines",
            sql: "prepared_quantity >= 0 AND prepared_quantity <= quantity");

        migrationBuilder.CreateTable(
            name: "units",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                unit_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_fulfilment_units",
                    x => new
                    {
                        x.order_id,
                        x.unit_id
                    });

                table.ForeignKey(
                    name: "fk_fulfilment_units_requests_order_id",
                    column: x => x.order_id,
                    principalSchema: "fulfilment",
                    principalTable: "requests",
                    principalColumn: "order_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "unit_lines",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                unit_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                order_line_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                quantity = table.Column<int>(
                    type: "integer",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_fulfilment_unit_lines",
                    x => new
                    {
                        x.order_id,
                        x.unit_id,
                        x.order_line_id
                    });

                table.CheckConstraint(
                    "ck_fulfilment_unit_line_quantity",
                    "quantity > 0");

                table.ForeignKey(
                    name: "fk_fulfilment_unit_lines_units_order_id_unit_id",
                    columns: x => new
                    {
                        x.order_id,
                        x.unit_id
                    },
                    principalSchema: "fulfilment",
                    principalTable: "units",
                    principalColumns: new[]
                    {
                        "order_id",
                        "unit_id"
                    },
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "unit_lines",
            schema: "fulfilment");

        migrationBuilder.DropTable(
            name: "units",
            schema: "fulfilment");

        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_line_prepared_quantity",
            schema: "fulfilment",
            table: "request_lines");

        migrationBuilder.DropColumn(
            name: "prepared_quantity",
            schema: "fulfilment",
            table: "request_lines");

        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_request_status",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_request_status",
            schema: "fulfilment",
            table: "requests",
            sql: "status = 0");
    }
}
