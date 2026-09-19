using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAuthorisationReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reconciled_at_utc",
                schema: "payments",
                table: "authorisation_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reconciliation_attempt_count",
                schema: "payments",
                table: "authorisation_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_authorisation_pending_not_reconciled",
                schema: "payments",
                table: "authorisation_attempts",
                sql: "status <> 0 OR reconciliation_attempt_count = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_authorisation_reconciliation_count",
                schema: "payments",
                table: "authorisation_attempts",
                sql: "reconciliation_attempt_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_authorisation_reconciliation_shape",
                schema: "payments",
                table: "authorisation_attempts",
                sql: "(reconciliation_attempt_count = 0 AND last_reconciled_at_utc IS NULL) OR (reconciliation_attempt_count > 0 AND last_reconciled_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_authorisation_reconciliation_time",
                schema: "payments",
                table: "authorisation_attempts",
                sql: "last_reconciled_at_utc IS NULL OR last_reconciled_at_utc >= requested_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_authorisation_pending_not_reconciled",
                schema: "payments",
                table: "authorisation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_authorisation_reconciliation_count",
                schema: "payments",
                table: "authorisation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_authorisation_reconciliation_shape",
                schema: "payments",
                table: "authorisation_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_authorisation_reconciliation_time",
                schema: "payments",
                table: "authorisation_attempts");

            migrationBuilder.DropColumn(
                name: "last_reconciled_at_utc",
                schema: "payments",
                table: "authorisation_attempts");

            migrationBuilder.DropColumn(
                name: "reconciliation_attempt_count",
                schema: "payments",
                table: "authorisation_attempts");
        }
    }
}
