using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Switchyard.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderingInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "ordering",
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
                    table.PrimaryKey("pk_ordering_inbox_messages", x => new { x.consumer_name, x.message_id });
                    table.CheckConstraint("ck_ordering_inbox_consumer_name", "btrim(consumer_name) <> ''");
                    table.CheckConstraint("ck_ordering_inbox_message_type", "btrim(message_type) <> ''");
                    table.CheckConstraint("ck_ordering_inbox_payload_hash", "char_length(payload_hash) = 64");
                    table.CheckConstraint("ck_ordering_inbox_processed_time", "processed_at_utc >= received_at_utc");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ordering_inbox_consumer_processed",
                schema: "ordering",
                table: "inbox_messages",
                columns: new[] { "consumer_name", "processed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "ordering");
        }
    }
}
