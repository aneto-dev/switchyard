using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261103102500_AddFulfilmentCancellation")]
public partial class AddFulfilmentCancellation : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "cancelled_quantity",
            schema: "fulfilment",
            table: "request_lines",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_line_cancelled_quantity",
            schema: "fulfilment",
            table: "request_lines",
            sql: "cancelled_quantity >= 0 AND prepared_quantity + cancelled_quantity <= quantity");

        migrationBuilder.CreateTable(
            name: "cancellations",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                cancellation_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                requested_at_utc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                outcome = table.Column<int>(
                    type: "integer",
                    nullable: false),
                rejection_reason = table.Column<int>(
                    type: "integer",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_fulfilment_cancellations",
                    x => new
                    {
                        x.order_id,
                        x.cancellation_id
                    });

                table.CheckConstraint(
                    "ck_fulfilment_cancellation_outcome",
                    "outcome IN (0, 1)");

                table.CheckConstraint(
                    "ck_fulfilment_cancellation_shape",
                    "(outcome = 0 AND rejection_reason IS NULL) OR (outcome = 1 AND rejection_reason = 0)");

                table.ForeignKey(
                    name: "fk_fulfilment_cancellations_requests_order_id",
                    column: x => x.order_id,
                    principalSchema: "fulfilment",
                    principalTable: "requests",
                    principalColumn: "order_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "cancellation_lines",
            schema: "fulfilment",
            columns: table => new
            {
                order_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                cancellation_id = table.Column<Guid>(
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
                    "pk_fulfilment_cancellation_lines",
                    x => new
                    {
                        x.order_id,
                        x.cancellation_id,
                        x.order_line_id
                    });

                table.CheckConstraint(
                    "ck_fulfilment_cancellation_line_quantity",
                    "quantity > 0");

                table.ForeignKey(
                    name: "fk_fulfilment_cancellation_lines_cancellations_order_id_cancellation_id",
                    columns: x => new
                    {
                        x.order_id,
                        x.cancellation_id
                    },
                    principalSchema: "fulfilment",
                    principalTable: "cancellations",
                    principalColumns: new[]
                    {
                        "order_id",
                        "cancellation_id"
                    },
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cancellation_lines",
            schema: "fulfilment");

        migrationBuilder.DropTable(
            name: "cancellations",
            schema: "fulfilment");

        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_line_cancelled_quantity",
            schema: "fulfilment",
            table: "request_lines");

        migrationBuilder.DropColumn(
            name: "cancelled_quantity",
            schema: "fulfilment",
            table: "request_lines");
    }
}
