using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Switchyard.Api.Ordering;
using Switchyard.Ordering.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.Api.Tests;

public sealed class OrderingEndpointTests
{
    [Fact]
    public async Task CreatesReplaysAndReadsOrderThroughHttpBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        using var application = CreateApplication(postgres.GetConnectionString());
        using var client = application.CreateClient();

        using var readinessResponse = await client.GetAsync("/health/ready", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);

        const string idempotencyKey = "checkout-api-001";

        using var createResponse = await client.SendAsync(
            CreateOrderRequest(idempotencyKey, quantity: 1),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken);

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.OrderId);
        Assert.Equal("SW-00100000", created.OrderNumber);
        Assert.Equal("Pending", created.Status);
        Assert.Equal(1299.99m, created.TotalAmount);
        Assert.Equal("GBP", created.Currency);
        Assert.Equal($"/api/orders/{created.OrderId:D}", createResponse.Headers.Location!.OriginalString);

        using var replayResponse = await client.SendAsync(
            CreateOrderRequest(idempotencyKey, quantity: 1),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(createResponse.Headers.Location, replayResponse.Headers.Location);

        var replayed = await replayResponse.Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken);

        Assert.NotNull(replayed);
        Assert.Equal(created.OrderId, replayed.OrderId);
        Assert.Equal(created.OrderNumber, replayed.OrderNumber);

        using var conflictResponse = await client.SendAsync(
            CreateOrderRequest(idempotencyKey, quantity: 2),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        var conflictBody = await conflictResponse.Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains(
            "The idempotency key has already been used with a different request.",
            conflictBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(idempotencyKey, conflictBody, StringComparison.Ordinal);

        using var getResponse = await client.GetAsync(createResponse.Headers.Location, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var order = await getResponse.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken);

        Assert.NotNull(order);
        Assert.Equal(created.OrderId, order.OrderId);
        Assert.Equal(created.OrderNumber, order.OrderNumber);
        Assert.Equal(created.TotalAmount, order.TotalAmount);
        Assert.Single(order.Lines);
        Assert.Equal("BIKE-001", order.Lines[0].SkuCode);
        Assert.Equal(1299.99m, order.Lines[0].LineTotalAmount);

        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(), "ordering.orders", cancellationToken));
        Assert.Equal(1, await CountRowsAsync(
            postgres.GetConnectionString(), "ordering.order_requests", cancellationToken));

        using var missingResponse = await client.GetAsync(
            $"/api/orders/{Guid.NewGuid():D}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRetriesCreateOneOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        using var application = CreateApplication(postgres.GetConnectionString());
        using var client = application.CreateClient();

        const string idempotencyKey = "checkout-concurrent-001";

        var firstTask = client.SendAsync(
            CreateOrderRequest(idempotencyKey, quantity: 1), cancellationToken);
        var secondTask = client.SendAsync(
            CreateOrderRequest(idempotencyKey, quantity: 1), cancellationToken);

        var responses = await Task.WhenAll(firstTask, secondTask);

        try
        {
            var statusCodes = responses.Select(response => response.StatusCode).ToArray();

            Assert.Contains(HttpStatusCode.Created, statusCodes);
            Assert.Contains(HttpStatusCode.OK, statusCodes);

            var first = await responses[0].Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken);
            var second = await responses[1].Content.ReadFromJsonAsync<CreateOrderResponse>(cancellationToken);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first.OrderId, second.OrderId);
            Assert.Equal(first.OrderNumber, second.OrderNumber);

            Assert.Equal(1, await CountRowsAsync(
                postgres.GetConnectionString(), "ordering.orders", cancellationToken));
            Assert.Equal(1, await CountRowsAsync(
                postgres.GetConnectionString(), "ordering.order_requests", cancellationToken));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task RejectsInvalidCreateRequestBeforePersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = application.CreateClient();

        using var request = CreateOrderRequest("checkout-invalid", quantity: 0);
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains("Quantity must be positive.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMissingIdempotencyKeyBeforePersistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
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
                        currency = "GBP"
                    }
                }
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains(
            "Exactly one Idempotency-Key header is required.",
            body,
            StringComparison.Ordinal);
    }

    private static HttpRequestMessage CreateOrderRequest(string idempotencyKey, int quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(
                new
                {
                    lines = new[]
                    {
                        new
                        {
                            skuCode = "BIKE-001",
                            productName = "Road Bike",
                            quantity,
                            unitPriceAmount = 1299.99m,
                            currency = "gbp"
                        }
                    }
                })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_ordering_api_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
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

    private static async Task ApplyMigrationsAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new OrderingDbContext(options);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<int> CountRowsAsync(
        string connectionString, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {tableName};", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
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
