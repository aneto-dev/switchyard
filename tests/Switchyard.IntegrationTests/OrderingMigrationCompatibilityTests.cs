using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OrderingMigrationCompatibilityTests
{
    [Fact]
    public async Task ReservationValidityMigrationPreservesLegacyReservedLineWithoutInventingExpiry()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            new PostgreSqlBuilder(
                "postgres:18-alpine")
                .WithDatabase(
                    "switchyard_ordering_migration_compatibility_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        var options =
            new DbContextOptionsBuilder<OrderingDbContext>()
                .UseNpgsql(
                    postgres.GetConnectionString())
                .Options;

        const string previousMigration =
            "20261002073452_AddExpiredOrderPlacementLineState";

        var orderId =
            Guid.NewGuid();

        var orderLineId =
            Guid.NewGuid();

        var reservationRequestId =
            Guid.NewGuid();

        var reservationId =
            Guid.NewGuid();

        var startedAtUtc =
            new DateTimeOffset(
                2026,
                10,
                2,
                17,
                0,
                0,
                TimeSpan.Zero);

        const string skuCode =
            "LEGACY-BIKE-001";

        const int quantity =
            1;

        await using (var legacyContext =
            new OrderingDbContext(options))
        {
            var migrator =
                legacyContext.GetService<IMigrator>();

            await migrator.MigrateAsync(
                previousMigration,
                cancellationToken);

            var appliedMigrations =
                await legacyContext.Database
                    .GetAppliedMigrationsAsync(
                        cancellationToken);

            Assert.Contains(
                previousMigration,
                appliedMigrations);

            Assert.DoesNotContain(
                appliedMigrations,
                migration =>
                    migration.EndsWith(
                        "_AddReservationValidityDeadline",
                        StringComparison.Ordinal));

            const string orderNumber =
                "SW-LEGACY-RESERVATION";

            await legacyContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ordering.orders
                (
                    id,
                    order_number,
                    created_at_utc,
                    status
                )
                VALUES
                (
                    {orderId},
                    {orderNumber},
                    {startedAtUtc},
                    {(int)OrderStatus.Pending}
                );
                """,
                cancellationToken);

            await legacyContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ordering.order_placement_processes
                (
                    order_id,
                    state,
                    started_at_utc,
                    updated_at_utc
                )
                VALUES
                (
                    {orderId},
                    {(int)OrderPlacementState.AwaitingInventory},
                    {startedAtUtc},
                    {startedAtUtc}
                );
                """,
                cancellationToken);


            await legacyContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ordering.order_placement_lines
                (
                    order_id,
                    order_line_id,
                    reservation_request_id,
                    sku_code,
                    quantity,
                    state,
                    reservation_id
                )
                VALUES
                (
                    {orderId},
                    {orderLineId},
                    {reservationRequestId},
                    {skuCode},
                    {quantity},
                    {(int)OrderPlacementLineState.Reserved},
                    {reservationId}
                );
                """,
                cancellationToken);
        }

        await using (var migrationContext =
            new OrderingDbContext(options))
        {
            await migrationContext.Database.MigrateAsync(
                cancellationToken);

            var appliedMigrations =
                await migrationContext.Database
                    .GetAppliedMigrationsAsync(
                        cancellationToken);

            Assert.Contains(
                appliedMigrations,
                migration =>
                    migration.EndsWith(
                        "_AddReservationValidityDeadline",
                        StringComparison.Ordinal));
        }

        await using (var readContext =
            new OrderingDbContext(options))
        {
            var repository =
                new EfOrderPlacementProcessRepository(
                    readContext);

            var process =
                await repository.GetByOrderIdAsync(
                    new OrderId(orderId),
                    cancellationToken);

            Assert.NotNull(
                process);

            Assert.Equal(
                OrderPlacementState.AwaitingInventory,
                process.State);

            var line =
                Assert.Single(
                    process.Lines);

            Assert.Equal(
                new OrderLineId(orderLineId),
                line.OrderLineId);

            Assert.Equal(
                reservationRequestId,
                line.ReservationRequestId);

            Assert.Equal(
                OrderPlacementLineState.Reserved,
                line.State);

            Assert.Equal(
                reservationId,
                line.ReservationId);

            Assert.Null(
                line.ReservationExpiresAtUtc);
        }

        var legacyExpiresAtUtc =
            startedAtUtc.AddMinutes(15);

        await using (var updateContext =
            new OrderingDbContext(options))
        {
            await using var transaction =
                await updateContext.Database.BeginTransactionAsync(
                    cancellationToken);

            var repository =
                new EfOrderPlacementProcessRepository(
                    updateContext);

            var process =
                await repository.GetByOrderIdForUpdateAsync(
                    new OrderId(orderId),
                    cancellationToken);

            Assert.NotNull(
                process);

            var transition =
                process.RecordInventoryReserved(
                    reservationRequestId,
                    new OrderLineId(orderLineId),
                    skuCode,
                    quantity,
                    reservationId,
                    legacyExpiresAtUtc,
                    Guid.NewGuid(),
                    startedAtUtc.AddMinutes(1));

            Assert.True(
                transition.Changed);

            Assert.False(
                transition.AuthorisePayment);

            Assert.False(
                transition.FailPlacement);

            Assert.Empty(
                transition.Releases);

            Assert.Equal(
                OrderPlacementState.AwaitingInventory,
                process.State);

            Assert.Equal(
                startedAtUtc.AddMinutes(1),
                process.UpdatedAtUtc);

            var enrichedLine =
                Assert.Single(
                    process.Lines);

            Assert.Equal(
                legacyExpiresAtUtc,
                enrichedLine.ReservationExpiresAtUtc);

            await repository.UpdateAsync(
                process,
                cancellationToken);

            await updateContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }

        await using (var verificationContext =
            new OrderingDbContext(options))
        {
            var repository =
                new EfOrderPlacementProcessRepository(
                    verificationContext);

            var process =
                await repository.GetByOrderIdAsync(
                    new OrderId(orderId),
                    cancellationToken);

            Assert.NotNull(
                process);

            var line =
                Assert.Single(
                    process.Lines);

            Assert.Equal(
                legacyExpiresAtUtc,
                line.ReservationExpiresAtUtc);

            Assert.Equal(
                startedAtUtc.AddMinutes(1),
                process.UpdatedAtUtc);

            Assert.Throws<OrderPlacementInventoryOutcomeException>(
                () =>
                    process.RecordInventoryReserved(
                        reservationRequestId,
                        new OrderLineId(orderLineId),
                        skuCode,
                        quantity,
                        reservationId,
                        legacyExpiresAtUtc.AddMinutes(1),
                        Guid.NewGuid(),
                        startedAtUtc.AddMinutes(2)));
        }
    }
}
