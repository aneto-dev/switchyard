using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSettlementReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_shape",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_status",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reconciled_at_utc",
                schema: "payments",
                table: "settlement_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reconciliation_attempt_count",
                schema: "payments",
                table: "settlement_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_pending_not_reconciled",
                schema: "payments",
                table: "settlement_attempts",
                sql: "status <> 0 OR reconciliation_attempt_count = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_reconciliation_count",
                schema: "payments",
                table: "settlement_attempts",
                sql: "reconciliation_attempt_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_reconciliation_shape",
                schema: "payments",
                table: "settlement_attempts",
                sql: "(reconciliation_attempt_count = 0 AND last_reconciled_at_utc IS NULL) OR (reconciliation_attempt_count > 0 AND last_reconciled_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_reconciliation_time",
                schema: "payments",
                table: "settlement_attempts",
                sql: "last_reconciled_at_utc IS NULL OR last_reconciled_at_utc >= requested_at_utc");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_shape",
                schema: "payments",
                table: "settlement_attempts",
                sql: "(status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL) OR (status = 1 AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL) OR (status IN (2, 3) AND resolved_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_status",
                schema: "payments",
                table: "settlement_attempts",
                sql: "status IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_pending_not_reconciled",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_reconciliation_count",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_reconciliation_shape",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_reconciliation_time",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_shape",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_settlement_status",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.Sql(
                """
                UPDATE payments.settlement_attempts
                SET status = 2
                WHERE status = 3;
                """);

            migrationBuilder.DropColumn(
                name: "last_reconciled_at_utc",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.DropColumn(
                name: "reconciliation_attempt_count",
                schema: "payments",
                table: "settlement_attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_shape",
                schema: "payments",
                table: "settlement_attempts",
                sql: "(status = 0 AND provider_reference IS NULL AND resolved_at_utc IS NULL) OR (status = 1 AND provider_reference IS NOT NULL AND resolved_at_utc IS NOT NULL) OR (status = 2 AND resolved_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_settlement_status",
                schema: "payments",
                table: "settlement_attempts",
                sql: "status IN (0, 1, 2)");
        }
    }
}
