using Microsoft.EntityFrameworkCore;
using Switchyard.Fulfilment.Application.Cancellations;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Application.Units;
using Switchyard.Fulfilment.Domain.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class FulfilmentCancellationTests
{
    [Fact]
    public async Task CancellationReducesOnlyRemainingCancellableQuantity()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var orderId =
            Guid.NewGuid();

        var lineId =
            Guid.NewGuid();

        await CreateRequestAsync(
            connectionString,
            orderId,
            lineId,
            quantity: 3,
            cancellationToken);

        await CreateUnitAsync(
            connectionString,
            orderId,
            lineId,
            quantity: 1,
            cancellationToken);

        var cancellationId =
            Guid.NewGuid();

        var first =
            await CancelAsync(
                connectionString,
                orderId,
                cancellationId,
                lineId,
                quantity: 1,
                cancellationToken);

        var replay =
            await CancelAsync(
                connectionString,
                orderId,
                cancellationId,
                lineId,
                quantity: 1,
                cancellationToken);

        Assert.Equal(
            FulfilmentCancellationOutcome.Accepted,
            first.Outcome);

        Assert.False(
            first.Replayed);

        Assert.True(
            replay.Replayed);

        await using var dbContext =
            CreateDbContext(
                connectionString);

        var request =
            await new EfFulfilmentRequestRepository(
                    dbContext)
                .GetByOrderIdAsync(
                    orderId,
                    cancellationToken);

        Assert.NotNull(
            request);

        var line =
            Assert.Single(
                request.Lines);

        Assert.Equal(
            1,
            line.PreparedQuantity);

        Assert.Equal(
            1,
            line.CancelledQuantity);

        Assert.Equal(
            1,
            line.CancellableQuantity);

        Assert.Single(
            request.Cancellations);
    }

    [Fact]
    public async Task CancellationAfterQuantityCrossesBoundaryIsDurablyRejected()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var orderId =
            Guid.NewGuid();

        var lineId =
            Guid.NewGuid();

        await CreateRequestAsync(
            connectionString,
            orderId,
            lineId,
            quantity: 1,
            cancellationToken);

        await CreateUnitAsync(
            connectionString,
            orderId,
            lineId,
            quantity: 1,
            cancellationToken);

        var cancellationId =
            Guid.NewGuid();

        var result =
            await CancelAsync(
                connectionString,
                orderId,
                cancellationId,
                lineId,
                quantity: 1,
                cancellationToken);

        Assert.Equal(
            FulfilmentCancellationOutcome.Rejected,
            result.Outcome);

        Assert.Equal(
            FulfilmentCancellationRejectionReason.QuantityNoLongerCancellable,
            result.RejectionReason);

        var replay =
            await CancelAsync(
                connectionString,
                orderId,
                cancellationId,
                lineId,
                quantity: 1,
                cancellationToken);

        Assert.True(
            replay.Replayed);

        Assert.Equal(
            FulfilmentCancellationOutcome.Rejected,
            replay.Outcome);
    }

    [Fact]
    public async Task PrepareAndCancelRaceCannotBothClaimLastQuantity()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var postgres =
            await StartPostgresAsync(
                cancellationToken);

        var connectionString =
            postgres.GetConnectionString();

        await ApplyMigrationsAsync(
            connectionString,
            cancellationToken);

        var orderId =
            Guid.NewGuid();

        var lineId =
            Guid.NewGuid();

        await CreateRequestAsync(
            connectionString,
            orderId,
            lineId,
            quantity: 1,
            cancellationToken);

        var prepareTask =
            Task.Run(
                async () =>
                {
                    try
                    {
                        await CreateUnitAsync(
                            connectionString,
                            orderId,
                            lineId,
                            quantity: 1,
                            cancellationToken);

                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                },
                cancellationToken);

        var cancellationTask =
            Task.Run(
                async () =>
                {
                    var result =
                        await CancelAsync(
                            connectionString,
                            orderId,
                            Guid.NewGuid(),
                            lineId,
                            quantity: 1,
                            cancellationToken);

                    return result.Outcome ==
                        FulfilmentCancellationOutcome.Accepted;
                },
                cancellationToken);

        var prepareWon =
            await prepareTask;

        var cancellationWon =
            await cancellationTask;

        Assert.NotEqual(
            prepareWon,
            cancellationWon);

        await using var dbContext =
            CreateDbContext(
                connectionString);

        var request =
            await new EfFulfilmentRequestRepository(
                    dbContext)
                .GetByOrderIdAsync(
                    orderId,
                    cancellationToken);

        Assert.NotNull(
            request);

        var line =
            Assert.Single(
                request.Lines);

        Assert.Equal(
            0,
            line.CancellableQuantity);

        Assert.Equal(
            1,
            line.PreparedQuantity +
            line.CancelledQuantity);
    }

    private static async Task CreateRequestAsync(
        string connectionString,
        Guid orderId,
        Guid lineId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new CreateFulfilmentHandler(
                new EfFulfilmentRequestRepository(
                    dbContext));

        await handler.HandleAsync(
            new CreateFulfilmentCommand(
                orderId,
                Guid.NewGuid(),
                $"SW-{orderId:N}"[..12],
                new[]
                {
                    new CreateFulfilmentLine(
                        lineId,
                        "BIKE-001",
                        "Road Bike",
                        quantity)
                },
                new DateTimeOffset(
                    2026,
                    11,
                    3,
                    10,
                    30,
                    0,
                    TimeSpan.Zero)),
            cancellationToken);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static async Task CreateUnitAsync(
        string connectionString,
        Guid orderId,
        Guid lineId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new CreateFulfilmentUnitHandler(
                new EfFulfilmentRequestRepository(
                    dbContext));

        await handler.HandleAsync(
            new CreateFulfilmentUnitCommand(
                orderId,
                Guid.NewGuid(),
                new[]
                {
                    new CreateFulfilmentUnitLine(
                        lineId,
                        quantity)
                },
                new DateTimeOffset(
                    2026,
                    11,
                    3,
                    10,
                    35,
                    0,
                    TimeSpan.Zero)),
            cancellationToken);
    }

    private static async Task<CancelFulfilmentResult> CancelAsync(
        string connectionString,
        Guid orderId,
        Guid cancellationId,
        Guid lineId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new CancelFulfilmentHandler(
                new EfFulfilmentRequestRepository(
                    dbContext));

        return await handler.HandleAsync(
            new CancelFulfilmentCommand(
                orderId,
                cancellationId,
                new[]
                {
                    new CancelFulfilmentLine(
                        lineId,
                        quantity)
                },
                new DateTimeOffset(
                    2026,
                    11,
                    3,
                    10,
                    40,
                    0,
                    TimeSpan.Zero)),
            cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres =
            new PostgreSqlBuilder(
                "postgres:18-alpine")
                .WithDatabase(
                    "switchyard_fulfilment_cancellation_test")
                .WithUsername(
                    "switchyard")
                .WithPassword(
                    "switchyard-test-only")
                .Build();

        await postgres.StartAsync(
            cancellationToken);

        return postgres;
    }

    private static FulfilmentDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<FulfilmentDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        return new FulfilmentDbContext(
            options);
    }

    private static async Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        await dbContext.Database.MigrateAsync(
            cancellationToken);
    }
}
