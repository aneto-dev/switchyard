using Microsoft.EntityFrameworkCore;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Application.Units;
using Switchyard.Fulfilment.Domain.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class FulfilmentProgressionTests
{
    [Fact]
    public async Task FulfilmentCanProgressAcrossMultipleUnits()
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

        var bikeLineId =
            Guid.NewGuid();

        var helmetLineId =
            Guid.NewGuid();

        await CreateRequestAsync(
            connectionString,
            orderId,
            bikeLineId,
            helmetLineId,
            cancellationToken);

        var first =
            await CreateUnitAsync(
                connectionString,
                new CreateFulfilmentUnitCommand(
                    orderId,
                    Guid.NewGuid(),
                    new[]
                    {
                        new CreateFulfilmentUnitLine(
                            bikeLineId,
                            1),
                        new CreateFulfilmentUnitLine(
                            helmetLineId,
                            1)
                    },
                    new DateTimeOffset(
                        2026,
                        11,
                        3,
                        10,
                        0,
                        0,
                        TimeSpan.Zero)),
                cancellationToken);

        Assert.False(
            first.Replayed);

        Assert.Equal(
            FulfilmentRequestStatus.PartiallyPrepared,
            first.Status);

        var second =
            await CreateUnitAsync(
                connectionString,
                new CreateFulfilmentUnitCommand(
                    orderId,
                    Guid.NewGuid(),
                    new[]
                    {
                        new CreateFulfilmentUnitLine(
                            helmetLineId,
                            1)
                    },
                    new DateTimeOffset(
                        2026,
                        11,
                        3,
                        10,
                        5,
                        0,
                        TimeSpan.Zero)),
                cancellationToken);

        Assert.Equal(
            FulfilmentRequestStatus.Prepared,
            second.Status);

        await using var dbContext =
            CreateDbContext(
                connectionString);

        var repository =
            new EfFulfilmentRequestRepository(
                dbContext);

        var request =
            await repository.GetByOrderIdAsync(
                orderId,
                cancellationToken);

        Assert.NotNull(
            request);

        Assert.Equal(
            2,
            request.Units.Count);

        Assert.Equal(
            0,
            request.Lines
                   .Single(
                       line =>
                           line.OrderLineId ==
                           bikeLineId)
                   .CancellableQuantity);

        Assert.Equal(
            0,
            request.Lines
                   .Single(
                       line =>
                           line.OrderLineId ==
                           helmetLineId)
                   .CancellableQuantity);
    }

    [Fact]
    public async Task ReplayingSameUnitIsIdempotent()
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

        var bikeLineId =
            Guid.NewGuid();

        var helmetLineId =
            Guid.NewGuid();

        await CreateRequestAsync(
            connectionString,
            orderId,
            bikeLineId,
            helmetLineId,
            cancellationToken);

        var unitId =
            Guid.NewGuid();

        var command =
            new CreateFulfilmentUnitCommand(
                orderId,
                unitId,
                new[]
                {
                    new CreateFulfilmentUnitLine(
                        bikeLineId,
                        1)
                },
                new DateTimeOffset(
                    2026,
                    11,
                    3,
                    10,
                    10,
                    0,
                    TimeSpan.Zero));

        var first =
            await CreateUnitAsync(
                connectionString,
                command,
                cancellationToken);

        var replay =
            await CreateUnitAsync(
                connectionString,
                command,
                cancellationToken);

        Assert.False(
            first.Replayed);

        Assert.True(
            replay.Replayed);

        await using var dbContext =
            CreateDbContext(
                connectionString);

        var repository =
            new EfFulfilmentRequestRepository(
                dbContext);

        var request =
            await repository.GetByOrderIdAsync(
                orderId,
                cancellationToken);

        Assert.NotNull(
            request);

        Assert.Single(
            request.Units);

        Assert.Equal(
            1,
            request.Lines
                   .Single(
                       line =>
                           line.OrderLineId ==
                           bikeLineId)
                   .PreparedQuantity);
    }

    [Fact]
    public async Task PreparingBeyondRemainingQuantityIsRejected()
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

        var bikeLineId =
            Guid.NewGuid();

        var helmetLineId =
            Guid.NewGuid();

        await CreateRequestAsync(
            connectionString,
            orderId,
            bikeLineId,
            helmetLineId,
            cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                CreateUnitAsync(
                    connectionString,
                    new CreateFulfilmentUnitCommand(
                        orderId,
                        Guid.NewGuid(),
                        new[]
                        {
                            new CreateFulfilmentUnitLine(
                                bikeLineId,
                                2)
                        },
                        new DateTimeOffset(
                            2026,
                            11,
                            3,
                            10,
                            15,
                            0,
                            TimeSpan.Zero)),
                    cancellationToken));

        await using var dbContext =
            CreateDbContext(
                connectionString);

        var repository =
            new EfFulfilmentRequestRepository(
                dbContext);

        var request =
            await repository.GetByOrderIdAsync(
                orderId,
                cancellationToken);

        Assert.NotNull(
            request);

        Assert.Empty(
            request.Units);

        Assert.All(
            request.Lines,
            line =>
                Assert.Equal(
                    0,
                    line.PreparedQuantity));
    }

    private static async Task CreateRequestAsync(
        string connectionString,
        Guid orderId,
        Guid bikeLineId,
        Guid helmetLineId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var repository =
            new EfFulfilmentRequestRepository(
                dbContext);

        var handler =
            new CreateFulfilmentHandler(
                repository);

        await handler.HandleAsync(
            new CreateFulfilmentCommand(
                orderId,
                Guid.NewGuid(),
                $"SW-{orderId:N}"[..12],
                new[]
                {
                    new CreateFulfilmentLine(
                        bikeLineId,
                        "BIKE-001",
                        "Road Bike",
                        1),
                    new CreateFulfilmentLine(
                        helmetLineId,
                        "HELMET-001",
                        "Helmet",
                        2)
                },
                new DateTimeOffset(
                    2026,
                    11,
                    3,
                    9,
                    45,
                    0,
                    TimeSpan.Zero)),
            cancellationToken);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static async Task<CreateFulfilmentUnitResult> CreateUnitAsync(
        string connectionString,
        CreateFulfilmentUnitCommand command,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            CreateDbContext(
                connectionString);

        var handler =
            new CreateFulfilmentUnitHandler(
                new EfFulfilmentRequestRepository(
                    dbContext));

        return await handler.HandleAsync(
            command,
            cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres =
            new PostgreSqlBuilder(
                "postgres:18-alpine")
                .WithDatabase(
                    "switchyard_fulfilment_progression_test")
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
