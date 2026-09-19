using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "payment_intents",
                schema: "payments",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    authorisation_status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorisation_resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_payment_intents", x => x.payment_id);
                    table.CheckConstraint("ck_payments_intent_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_intent_authorisation_shape", "(authorisation_status = 0 AND authorisation_resolved_at_utc IS NULL) OR (authorisation_status IN (1, 2, 3) AND authorisation_resolved_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_payments_intent_authorisation_status", "authorisation_status IN (0, 1, 2, 3)");
                    table.CheckConstraint("ck_payments_intent_authorisation_time", "authorisation_resolved_at_utc IS NULL OR authorisation_resolved_at_utc >= created_at_utc");
                    table.CheckConstraint("ck_payments_intent_currency", "currency ~ '^[A-Z]{3}$'");
                });

            migrationBuilder.CreateTable(
                name: "authorisation_attempts",
                schema: "payments",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments_authorisation_attempts", x => x.request_id);
                    table.CheckConstraint("ck_payments_authorisation_provider_key", "btrim(provider_idempotency_key) <> ''");
                    table.CheckConstraint("ck_payments_authorisation_provider_reference", "provider_reference IS NULL OR btrim(provider_reference) <> ''");
                    table.CheckConstraint("ck_payments_authorisation_shape", "(status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL) OR (status IN (1, 2) AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL) OR (status = 3 AND resolved_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_payments_authorisation_status", "status IN (0, 1, 2, 3)");
                    table.CheckConstraint("ck_payments_authorisation_time", "resolved_at_utc IS NULL OR resolved_at_utc >= requested_at_utc");
                    table.ForeignKey(
                        name: "fk_payments_authorisation_payment_intent",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payment_intents",
                        principalColumn: "payment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payments_authorisation_payment",
                schema: "payments",
                table: "authorisation_attempts",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_authorisation_provider_key",
                schema: "payments",
                table: "authorisation_attempts",
                column: "provider_idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_authorisation_provider_reference",
                schema: "payments",
                table: "authorisation_attempts",
                column: "provider_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_payment_intent_order",
                schema: "payments",
                table: "payment_intents",
                column: "order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorisation_attempts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_intents",
                schema: "payments");
        }
    }
}
