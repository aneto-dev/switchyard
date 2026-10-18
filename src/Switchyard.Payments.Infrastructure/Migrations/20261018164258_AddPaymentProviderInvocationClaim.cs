using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProviderInvocationClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_invocation_claimed_at_utc",
                schema: "payments",
                table: "authorisation_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_authorisation_provider_invocation_time",
                schema: "payments",
                table: "authorisation_attempts",
                sql: "provider_invocation_claimed_at_utc IS NULL OR provider_invocation_claimed_at_utc >= requested_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_authorisation_provider_invocation_time",
                schema: "payments",
                table: "authorisation_attempts");

            migrationBuilder.DropColumn(
                name: "provider_invocation_claimed_at_utc",
                schema: "payments",
                table: "authorisation_attempts");
        }
    }
}
