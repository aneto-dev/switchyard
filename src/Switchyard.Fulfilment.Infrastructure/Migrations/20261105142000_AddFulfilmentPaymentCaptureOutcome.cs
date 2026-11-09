using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261105142000_AddFulfilmentPaymentCaptureOutcome")]
public partial class AddFulfilmentPaymentCaptureOutcome : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "payment_capture_status",
            schema: "fulfilment",
            table: "requests",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "payment_capture_provider_reference",
            schema: "fulfilment",
            table: "requests",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "payment_capture_resolved_at_utc",
            schema: "fulfilment",
            table: "requests",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE fulfilment.requests
            SET payment_capture_status = 1
            WHERE payment_capture_request_id IS NOT NULL;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_capture_status",
            schema: "fulfilment",
            table: "requests",
            sql: "payment_capture_status BETWEEN 0 AND 4");

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_capture_shape",
            schema: "fulfilment",
            table: "requests",
            sql:
                "(payment_capture_request_id IS NULL AND payment_capture_requested_at_utc IS NULL AND payment_capture_status = 0 AND payment_capture_provider_reference IS NULL AND payment_capture_resolved_at_utc IS NULL) OR " +
                "(payment_capture_request_id IS NOT NULL AND payment_capture_requested_at_utc IS NOT NULL AND payment_capture_status = 1 AND payment_capture_provider_reference IS NULL AND payment_capture_resolved_at_utc IS NULL) OR " +
                "(payment_capture_request_id IS NOT NULL AND payment_capture_requested_at_utc IS NOT NULL AND payment_capture_status = 2 AND payment_capture_provider_reference IS NOT NULL AND payment_capture_resolved_at_utc IS NOT NULL) OR " +
                "(payment_capture_request_id IS NOT NULL AND payment_capture_requested_at_utc IS NOT NULL AND payment_capture_status IN (3, 4) AND payment_capture_resolved_at_utc IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_fulfilment_capture_resolved_time",
            schema: "fulfilment",
            table: "requests",
            sql: "payment_capture_resolved_at_utc IS NULL OR payment_capture_resolved_at_utc >= payment_capture_requested_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_capture_resolved_time",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_capture_shape",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.DropCheckConstraint(
            name: "ck_fulfilment_capture_status",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.DropColumn(
            name: "payment_capture_resolved_at_utc",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.DropColumn(
            name: "payment_capture_provider_reference",
            schema: "fulfilment",
            table: "requests");

        migrationBuilder.DropColumn(
            name: "payment_capture_status",
            schema: "fulfilment",
            table: "requests");
    }
}
