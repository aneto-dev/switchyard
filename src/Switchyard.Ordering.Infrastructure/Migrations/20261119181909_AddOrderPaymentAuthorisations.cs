using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentAuthorisations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_payment_authorisations",
                schema: "ordering",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reconciliation_attempt_count = table.Column<int>(type: "integer", nullable: true),
                    outcome_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_payment_authorisations", x => x.order_id);
                    table.CheckConstraint("ck_order_payment_authorisations_amount", "amount >= 0");
                    table.CheckConstraint("ck_order_payment_authorisations_attempts", "reconciliation_attempt_count IS NULL OR reconciliation_attempt_count >= 0");
                    table.CheckConstraint("ck_order_payment_authorisations_currency", "char_length(currency) = 3");
                    table.CheckConstraint("ck_order_payment_authorisations_outcome", "outcome BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_order_payment_authorisations_payment_shape", "payment_id IS NOT NULL OR outcome = 3");
                    table.ForeignKey(
                        name: "FK_order_payment_authorisations_order_placement_processes",
                        column: x => x.order_id,
                        principalSchema: "ordering",
                        principalTable: "order_placement_processes",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_order_payment_authorisations_request_id",
                schema: "ordering",
                table: "order_payment_authorisations",
                column: "request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_payment_authorisations",
                schema: "ordering");
        }
    }
}
