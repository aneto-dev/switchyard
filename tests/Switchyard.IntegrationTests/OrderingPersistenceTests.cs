using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Domain.Orders;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class OrderingPersistenceTests
{
    [Fact]
    public async Task InitialMigrationAndRepositoryRoundTripOrderAgainstPostgreSql18()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_ordering_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using (var migrationContext = new OrderingDbContext(options))
        {
            await migrationContext.Database.MigrateAsync(cancellationToken);

            var appliedMigrations = await migrationContext.Database
                .GetAppliedMigrationsAsync(cancellationToken);

            Assert.Contains("20260916210000_InitialOrdering", appliedMigrations);
            Assert.Contains(
                appliedMigrations,
                migration => migration.EndsWith("_AddOrderRequestIdempotency", StringComparison.Ordinal));
        }

        Order persistedOrder;

        await using (var writeContext = new OrderingDbContext(options))
        {
            var numberGenerator = new PostgresOrderNumberGenerator(writeContext);
            var firstNumber = await numberGenerator.NextAsync(cancellationToken);
            var secondNumber = await numberGenerator.NextAsync(cancellationToken);

            Assert.Equal("SW-00100000", firstNumber.Value);
            Assert.Equal("SW-00100001", secondNumber.Value);

            persistedOrder = Order.Create(
                OrderId.New(),
                firstNumber,
                new[]
                {
                    OrderLine.Create(
                        OrderLineId.New(),
                        new ProductSnapshot(new SkuCode("BIKE-001"), "Road Bike"),
                        1,
                        Money.Gbp(1299.99m)),
                    OrderLine.Create(
                        OrderLineId.New(),
                        new ProductSnapshot(new SkuCode("HELMET-001"), "Road Helmet"),
                        2,
                        Money.Gbp(79.50m))
                },
                new DateTimeOffset(2026, 9, 16, 20, 0, 0, TimeSpan.Zero));

            var repository = new EfOrderRepository(writeContext);
            var unitOfWork = new EfOrderingUnitOfWork(writeContext);

            await repository.AddAsync(persistedOrder, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await using (var readContext = new OrderingDbContext(options))
        {
            var repository = new EfOrderRepository(readContext);
            var loadedOrder = await repository.GetByIdAsync(persistedOrder.Id, cancellationToken);

            Assert.NotNull(loadedOrder);
            Assert.Equal(persistedOrder.Id, loadedOrder!.Id);
            Assert.Equal(persistedOrder.OrderNumber, loadedOrder.OrderNumber);
            Assert.Equal(OrderStatus.Pending, loadedOrder.Status);
            Assert.Equal(persistedOrder.CreatedAtUtc, loadedOrder.CreatedAtUtc);
            Assert.Equal(1458.99m, loadedOrder.Total.Amount);
            Assert.Equal("GBP", loadedOrder.Total.Currency);
            Assert.Equal(2, loadedOrder.Lines.Count);
            Assert.Equal("BIKE-001", loadedOrder.Lines[0].Product.SkuCode.Value);
            Assert.Equal("HELMET-001", loadedOrder.Lines[1].Product.SkuCode.Value);
        }

        await using (var duplicateContext = new OrderingDbContext(options))
        {
            var duplicate = Order.Create(
                OrderId.New(),
                persistedOrder.OrderNumber,
                new[]
                {
                    OrderLine.Create(
                        OrderLineId.New(),
                        new ProductSnapshot(new SkuCode("BIKE-002"), "Gravel Bike"),
                        1,
                        Money.Gbp(999m))
                },
                DateTimeOffset.UtcNow);

            var repository = new EfOrderRepository(duplicateContext);
            var unitOfWork = new EfOrderingUnitOfWork(duplicateContext);

            await repository.AddAsync(duplicate, cancellationToken);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => unitOfWork.SaveChangesAsync(cancellationToken));
        }
    }
}
