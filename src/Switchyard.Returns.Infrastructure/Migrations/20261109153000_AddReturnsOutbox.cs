using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Returns.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Returns.Infrastructure.Migrations;

[DbContext(typeof(ReturnsDbContext))]
[Migration("20261109153000_AddReturnsOutbox")]
public sealed class AddReturnsOutbox : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE returns.outbox_messages
            (
                message_id uuid NOT NULL,
                message_type varchar(200) NOT NULL,
                payload jsonb NOT NULL,
                occurred_at_utc timestamptz NOT NULL,
                correlation_id uuid NOT NULL,
                causation_id uuid NULL,
                available_at_utc timestamptz NOT NULL,
                delivery_attempt_count integer NOT NULL DEFAULT 0,
                last_attempt_at_utc timestamptz NULL,
                lock_token uuid NULL,
                locked_until_utc timestamptz NULL,
                published_at_utc timestamptz NULL,
                last_error varchar(200) NULL,
                CONSTRAINT pk_returns_outbox_messages
                    PRIMARY KEY (message_id),
                CONSTRAINT ck_returns_outbox_message_type
                    CHECK (btrim(message_type) <> ''),
                CONSTRAINT ck_returns_outbox_delivery_attempt_count
                    CHECK (delivery_attempt_count >= 0),
                CONSTRAINT ck_returns_outbox_attempt_shape
                    CHECK (
                        (delivery_attempt_count = 0 AND last_attempt_at_utc IS NULL)
                        OR
                        (delivery_attempt_count > 0 AND last_attempt_at_utc IS NOT NULL)
                    ),
                CONSTRAINT ck_returns_outbox_lock_shape
                    CHECK (
                        (lock_token IS NULL AND locked_until_utc IS NULL)
                        OR
                        (lock_token IS NOT NULL AND locked_until_utc IS NOT NULL)
                    ),
                CONSTRAINT ck_returns_outbox_published_time
                    CHECK (
                        published_at_utc IS NULL
                        OR published_at_utc >= occurred_at_utc
                    )
            );

            CREATE INDEX ix_returns_outbox_due
                ON returns.outbox_messages (
                    available_at_utc,
                    locked_until_utc,
                    occurred_at_utc
                )
                WHERE published_at_utc IS NULL;
            """);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS returns.outbox_messages;
            """);
    }
}
