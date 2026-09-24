using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "inventory",
                columns: table => new
                {
                    consumer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_inbox_messages", x => new { x.consumer_name, x.message_id });
                    table.CheckConstraint("ck_inventory_inbox_consumer_name", "btrim(consumer_name) <> ''");
                    table.CheckConstraint("ck_inventory_inbox_message_type", "btrim(message_type) <> ''");
                    table.CheckConstraint("ck_inventory_inbox_payload_hash", "char_length(payload_hash) = 64");
                    table.CheckConstraint("ck_inventory_inbox_processed_time", "processed_at_utc >= received_at_utc");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "inventory",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_outbox_messages", x => x.message_id);
                    table.CheckConstraint("ck_inventory_outbox_attempt_shape", "(delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL) OR (delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_inventory_outbox_available_time", "available_at_utc >= occurred_at_utc");
                    table.CheckConstraint("ck_inventory_outbox_delivery_attempt_count", "delivery_attempt_count >= 0");
                    table.CheckConstraint("ck_inventory_outbox_lock_shape", "(lock_token IS NULL AND locked_until_utc IS NULL) OR (lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)");
                    table.CheckConstraint("ck_inventory_outbox_lock_time", "locked_until_utc IS NULL OR last_attempt_at_utc IS NOT NULL");
                    table.CheckConstraint("ck_inventory_outbox_message_type", "btrim(message_type) <> ''");
                    table.CheckConstraint("ck_inventory_outbox_published_not_locked", "published_at_utc IS NULL OR (lock_token IS NULL AND locked_until_utc IS NULL)");
                    table.CheckConstraint("ck_inventory_outbox_published_time", "published_at_utc IS NULL OR published_at_utc >= occurred_at_utc");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_inbox_consumer_processed",
                schema: "inventory",
                table: "inbox_messages",
                columns: new[] { "consumer_name", "processed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_outbox_due",
                schema: "inventory",
                table: "outbox_messages",
                columns: new[] { "available_at_utc", "locked_until_utc", "occurred_at_utc" },
                filter: "published_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "inventory");
        }
    }
}
