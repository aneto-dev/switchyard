using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCaptureAndVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement_attempts",
                schema: "payments",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provider_idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_settlement_attempts", x => x.request_id);
                    table.CheckConstraint("ck_payments_settlement_action", "action IN (0, 1)");
                    table.CheckConstraint("ck_payments_settlement_provider_key", "btrim(provider_idempotency_key) <> ''");
                    table.CheckConstraint("ck_payments_settlement_provider_reference", "provider_reference IS NULL OR btrim(provider_reference) <> ''");
                    table.CheckConstraint("ck_payments_settlement_shape", "(status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL) OR (status = 1 AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL) OR (status = 2 AND resolved_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_payments_settlement_status", "status IN (0, 1, 2)");
                    table.CheckConstraint("ck_payments_settlement_time", "resolved_at_utc IS NULL OR resolved_at_utc >= requested_at_utc");
                    table.ForeignKey(
                        name: "fk_payments_settlement_payment_intent",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payment_intents",
                        principalColumn: "payment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payments_settlement_payment",
                schema: "payments",
                table: "settlement_attempts",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_settlement_provider_key",
                schema: "payments",
                table: "settlement_attempts",
                column: "provider_idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_settlement_provider_reference",
                schema: "payments",
                table: "settlement_attempts",
                column: "provider_reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_attempts",
                schema: "payments");
        }
    }
}
