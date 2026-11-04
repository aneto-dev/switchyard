using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261103120500_AddFulfilmentOutbox")]
public partial class AddFulfilmentOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "fulfilment",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                delivery_attempt_count = table.Column<int>(type: "integer", nullable: false),
                last_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fulfilment_outbox_messages", x => x.message_id);
                table.CheckConstraint("ck_fulfilment_outbox_message_type", "btrim(message_type) <> ''");
                table.CheckConstraint("ck_fulfilment_outbox_delivery_attempt_count", "delivery_attempt_count >= 0");
                table.CheckConstraint("ck_fulfilment_outbox_attempt_shape", "(delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL) OR (delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)");
                table.CheckConstraint("ck_fulfilment_outbox_lock_shape", "(lock_token IS NULL AND locked_until_utc IS NULL) OR (lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)");
                table.CheckConstraint("ck_fulfilment_outbox_published_time", "published_at_utc IS NULL OR published_at_utc >= occurred_at_utc");
            });

        migrationBuilder.CreateIndex(
            name: "ix_fulfilment_outbox_due",
            schema: "fulfilment",
            table: "outbox_messages",
            columns: new[] { "available_at_utc", "locked_until_utc", "occurred_at_utc" },
            filter: "published_at_utc IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "fulfilment");
    }
}
