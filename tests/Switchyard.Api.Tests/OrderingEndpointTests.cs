using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Switchyard.Api.Ordering;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.Api.Tests;

public sealed class OrderingEndpointTests
{
    [Fact]
    public async Task CreatesAndReadsOrderThroughHttpBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_ordering_api_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        using var application = CreateApplication(postgres.GetConnectionString());
        using var client = application.CreateClient();

        using var readinessResponse = await client.GetAsync("/health/ready", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                lines = new[]
                {
                    new
                    {
                        skuCode = "BIKE-001",
                        productName = "Road Bike",
                        quantity = 1,
                        unitPriceAmount = 1299.99m,
                        currency = "gbp"
                    },
                    new
                    {
                        skuCode = "HELMET-001",
                        productName = "Road Helmet",
                        quantity = 2,
                        unitPriceAmount = 79.50m,
                        currency = "GBP"
                    }
                }
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken);

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.OrderId);
        Assert.Equal("SW-00100000", created.OrderNumber);
        Assert.Equal("Pending", created.Status);
        Assert.Equal(1458.99m, created.TotalAmount);
        Assert.Equal("GBP", created.Currency);
        Assert.Equal($"/api/orders/{created.OrderId:D}", createResponse.Headers.Location!.OriginalString);

        using var getResponse = await client.GetAsync(createResponse.Headers.Location, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var order = await getResponse.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken);

        Assert.NotNull(order);
        Assert.Equal(created.OrderId, order.OrderId);
        Assert.Equal(created.OrderNumber, order.OrderNumber);
        Assert.Equal(created.TotalAmount, order.TotalAmount);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal("BIKE-001", order.Lines[0].SkuCode);
        Assert.Equal(1299.99m, order.Lines[0].LineTotalAmount);
        Assert.Equal("HELMET-001", order.Lines[1].SkuCode);
        Assert.Equal(159.00m, order.Lines[1].LineTotalAmount);

        using var missingResponse = await client.GetAsync($"/api/orders/{Guid.NewGuid():D}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidCreateRequestBeforePersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                lines = Array.Empty<object>()
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains("At least one order line is required.", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateApplication(string connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<OrderingDatabaseOptions>(
                        options => options.ConnectionString = connectionString);
                });
            });
    }

    private static async Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new OrderingDbContext(options);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private sealed record CreateOrderResponse(
        Guid OrderId, string OrderNumber, string Status, decimal TotalAmount,
        string Currency, DateTimeOffset CreatedAtUtc);

    private sealed record OrderResponse(
        Guid OrderId, string OrderNumber, string Status, decimal TotalAmount,
        string Currency, DateTimeOffset CreatedAtUtc, IReadOnlyList<OrderLineResponse> Lines);

    private sealed record OrderLineResponse(
        Guid OrderLineId, string SkuCode, string ProductName, int Quantity,
        decimal UnitPriceAmount, decimal LineTotalAmount, string Currency);
}
