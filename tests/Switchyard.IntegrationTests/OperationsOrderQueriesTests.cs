using Microsoft.EntityFrameworkCore;
using Npgsql;
using Switchyard.Ordering.Application.Operations;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OperationsOrderQueriesTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListsRecentOrdersWithStatusFilterAndOrderNumberLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var oldest = await CreateOrderAsync(connectionString, BaseTime, "ops-list-1", cancellationToken);
        var failed = await CreateOrderAsync(connectionString, BaseTime.AddMinutes(1), "ops-list-2", cancellationToken);
        var stuck = await CreateOrderAsync(connectionString, BaseTime.AddMinutes(2), "ops-list-3", cancellationToken);
        var legacy = await CreateOrderAsync(connectionString, BaseTime.AddMinutes(3), "ops-list-4", cancellationToken);

        await ExecuteAsync(
            connectionString,
            $"UPDATE ordering.orders SET status = {(int)OrderStatus.Failed} WHERE id = '{failed.OrderId}';" +
            $"UPDATE ordering.order_placement_processes SET state = {(int)OrderPlacementState.Failed}, " +
            $"updated_at_utc = started_at_utc + interval '5 minutes' WHERE order_id = '{failed.OrderId}';" +
            $"UPDATE ordering.order_placement_processes SET state = {(int)OrderPlacementState.NeedsAttention} " +
            $"WHERE order_id = '{stuck.OrderId}';" +
            // Orders accepted before the placement process existed have no process row.
            $"DELETE FROM ordering.order_placement_processes WHERE order_id = '{legacy.OrderId}';",
            cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var queries = new EfOperationsOrderQueries(dbContext);

        var recent = await queries.ListRecentAsync(
            new OperationsOrderListFilter(null, null, OperationsOrderListFilter.DefaultLimit), cancellationToken);

        Assert.Equal(
            [legacy.OrderId, stuck.OrderId, failed.OrderId, oldest.OrderId],
            recent.Select(order => order.OrderId));

        var stuckSummary = recent[1];
        Assert.Equal(OrderStatus.Pending, stuckSummary.Status);
        Assert.Equal(OrderPlacementState.NeedsAttention, stuckSummary.PlacementState);
        Assert.Equal(1399.98m, stuckSummary.TotalAmount);
        Assert.Equal("GBP", stuckSummary.Currency);
        Assert.Equal(2, stuckSummary.LineCount);

        var failedSummary = recent[2];
        Assert.Equal(OrderStatus.Failed, failedSummary.Status);
        Assert.Equal(BaseTime.AddMinutes(6), failedSummary.PlacementUpdatedAtUtc);

        Assert.Null(recent[0].PlacementState);
        Assert.Null(recent[0].PlacementUpdatedAtUtc);

        var limited = await queries.ListRecentAsync(
            new OperationsOrderListFilter(null, null, 2), cancellationToken);
        Assert.Equal([legacy.OrderId, stuck.OrderId], limited.Select(order => order.OrderId));

        var failedOnly = await queries.ListRecentAsync(
            new OperationsOrderListFilter(OrderStatus.Failed, null, 10), cancellationToken);
        Assert.Equal([failed.OrderId], failedOnly.Select(order => order.OrderId));

        var lookedUp = await queries.ListRecentAsync(
            new OperationsOrderListFilter(null, $"  {stuck.OrderNumber.ToLowerInvariant()} ", 10), cancellationToken);
        Assert.Equal([stuck.OrderId], lookedUp.Select(order => order.OrderId));

        var wrongStatus = await queries.ListRecentAsync(
            new OperationsOrderListFilter(OrderStatus.Confirmed, stuck.OrderNumber, 10), cancellationToken);
        Assert.Empty(wrongStatus);

        var unknown = await queries.ListRecentAsync(
            new OperationsOrderListFilter(null, "SW-99999999", 10), cancellationToken);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task ReadsOrderDetailWithLineReservationOutcomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var order = await CreateOrderAsync(connectionString, BaseTime, "ops-detail-1", cancellationToken);
        var legacy = await CreateOrderAsync(connectionString, BaseTime.AddMinutes(1), "ops-detail-2", cancellationToken);

        List<Guid> lineIds;

        await using (var seedContext = CreateDbContext(connectionString))
        {
            lineIds = await seedContext.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM ordering.order_lines WHERE order_id = {order.OrderId} ORDER BY position")
                .ToListAsync(cancellationToken);
        }

        var reservationId = Guid.NewGuid();
        var paymentRequestId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var expiresAtUtc = BaseTime.AddMinutes(15);

        // First line was reserved and is now awaiting release; the second was rejected.
        await ExecuteAsync(
            connectionString,
            $"UPDATE ordering.order_placement_processes SET state = {(int)OrderPlacementState.CompensatingInventory}, " +
            $"updated_at_utc = started_at_utc + interval '30 seconds' WHERE order_id = '{order.OrderId}';" +
            $"UPDATE ordering.order_placement_lines SET state = {(int)OrderPlacementLineState.AwaitingRelease}, " +
            $"reservation_id = '{reservationId}', reservation_expires_at_utc = '{expiresAtUtc:O}' " +
            $"WHERE order_line_id = '{lineIds[0]}';" +
            $"UPDATE ordering.order_placement_lines SET state = {(int)OrderPlacementLineState.Rejected} " +
            $"WHERE order_line_id = '{lineIds[1]}';" +
            $"DELETE FROM ordering.order_placement_processes WHERE order_id = '{legacy.OrderId}';" +
            "INSERT INTO ordering.order_payment_authorisations " +
            "(order_id, request_id, payment_id, outcome, amount, currency, provider_reference, reason, " +
            "reconciliation_attempt_count, outcome_at_utc, recorded_at_utc) VALUES " +
            $"('{order.OrderId}', '{paymentRequestId}', '{paymentId}', {(int)PaymentAuthorisationOutcome.Failed}, " +
            $"1399.98, 'GBP', 'sim-decline-001', 'Declined', NULL, '{BaseTime.AddSeconds(25):O}', " +
            $"'{BaseTime.AddSeconds(30):O}');",
            cancellationToken);

        await using var dbContext = CreateDbContext(connectionString);
        var queries = new EfOperationsOrderQueries(dbContext);

        var detail = await queries.GetAsync(order.OrderId, cancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(order.OrderNumber, detail.OrderNumber);
        Assert.Equal(OrderStatus.Pending, detail.Status);
        Assert.Equal(1399.98m, detail.TotalAmount);
        Assert.Equal("GBP", detail.Currency);
        Assert.NotNull(detail.Placement);
        Assert.Equal(OrderPlacementState.CompensatingInventory, detail.Placement.State);
        Assert.Equal(BaseTime, detail.Placement.StartedAtUtc);
        Assert.Equal(BaseTime.AddSeconds(30), detail.Placement.UpdatedAtUtc);

        Assert.NotNull(detail.Payment);
        Assert.Equal(PaymentAuthorisationOutcome.Failed, detail.Payment.Outcome);
        Assert.Equal(paymentRequestId, detail.Payment.RequestId);
        Assert.Equal(paymentId, detail.Payment.PaymentId);
        Assert.Equal(1399.98m, detail.Payment.Amount);
        Assert.Equal("sim-decline-001", detail.Payment.ProviderReference);
        Assert.Equal("Declined", detail.Payment.Reason);
        Assert.Null(detail.Payment.ReconciliationAttemptCount);
        Assert.Equal(BaseTime.AddSeconds(25), detail.Payment.OutcomeAtUtc);
        Assert.Equal(BaseTime.AddSeconds(30), detail.Payment.RecordedAtUtc);

        Assert.Equal(lineIds, detail.Lines.Select(line => line.OrderLineId));

        var reserved = detail.Lines[0];
        Assert.Equal("BIKE-001", reserved.SkuCode);
        Assert.Equal(OrderPlacementLineState.AwaitingRelease, reserved.ReservationState);
        Assert.Equal(reservationId, reserved.ReservationId);
        Assert.Equal(expiresAtUtc, reserved.ReservationExpiresAtUtc);
        Assert.NotNull(reserved.ReservationRequestId);

        var rejected = detail.Lines[1];
        Assert.Equal("HELMET-001", rejected.SkuCode);
        Assert.Equal(99.99m, rejected.LineTotalAmount);
        Assert.Equal(OrderPlacementLineState.Rejected, rejected.ReservationState);
        Assert.Null(rejected.ReservationId);
        Assert.Null(rejected.ReservationExpiresAtUtc);

        var legacyDetail = await queries.GetAsync(legacy.OrderId, cancellationToken);

        Assert.NotNull(legacyDetail);
        Assert.Null(legacyDetail.Placement);
        Assert.Null(legacyDetail.Payment);
        Assert.All(legacyDetail.Lines, line =>
        {
            Assert.Null(line.ReservationState);
            Assert.Null(line.ReservationRequestId);
        });

        Assert.Null(await queries.GetAsync(Guid.NewGuid(), cancellationToken));
        Assert.Null(await queries.GetAsync(Guid.Empty, cancellationToken));
    }

    private static async Task<CreatedOrder> CreateOrderAsync(
        string connectionString, DateTimeOffset now, string idempotencyKey, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);

        var handler = new CreatePendingOrderHandler(
            new EfOrderRepository(dbContext),
            new EfOrderRequestRepository(dbContext),
            new EfOrderPlacementProcessRepository(dbContext),
            new EfOrderingUnitOfWork(dbContext),
            new EfOrderingOutboxStore(dbContext),
            new PostgresOrderNumberGenerator(dbContext),
            new FixedTimeProvider(now));

        var result = await handler.HandleAsync(
            new CreatePendingOrderCommand(
                idempotencyKey,
                [
                    new CreatePendingOrderLine("BIKE-001", "Road Bike", 1, 1299.99m, "GBP"),
                    new CreatePendingOrderLine("HELMET-001", "Helmet", 1, 99.99m, "GBP")
                ]),
            cancellationToken);

        return new CreatedOrder(result.OrderId, result.OrderNumber);
    }

    private static async Task ExecuteAsync(
        string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OrderingDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderingDbContext(options);
    }

    private static async Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_operations_orders_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
    }

    private sealed record CreatedOrder(Guid OrderId, string OrderNumber);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
