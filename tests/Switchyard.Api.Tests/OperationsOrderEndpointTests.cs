using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Switchyard.Api.Operations;
using Switchyard.Api.Ordering;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.Ordering.Infrastructure.Persistence;
using Switchyard.Returns.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Switchyard.Api.Tests;

public sealed class OperationsOrderEndpointTests
{
    [Fact]
    public async Task ListsFiltersAndReadsOrdersThroughOperationsBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        using var application = CreateApplication(postgres.GetConnectionString(), "Development");
        using var client = application.CreateClient();

        var first = await CreateOrderAsync(client, "ops-api-001", cancellationToken);
        var second = await CreateOrderAsync(client, "ops-api-002", cancellationToken);

        var recent = await client.GetFromJsonAsync<OrderSummary[]>("/api/operations/orders", cancellationToken);

        Assert.NotNull(recent);
        Assert.Equal([second.OrderId, first.OrderId], recent.Select(order => order.OrderId));
        Assert.All(recent, order =>
        {
            Assert.Equal("Pending", order.Status);
            Assert.Equal("AwaitingInventory", order.PlacementState);
            Assert.Equal(1, order.LineCount);
            Assert.Equal(1299.99m, order.TotalAmount);
            Assert.NotNull(order.PlacementUpdatedAtUtc);
        });

        var pending = await client.GetFromJsonAsync<OrderSummary[]>(
            "/api/operations/orders?status=pending&limit=1", cancellationToken);

        Assert.NotNull(pending);
        Assert.Equal([second.OrderId], pending.Select(order => order.OrderId));

        var confirmed = await client.GetFromJsonAsync<OrderSummary[]>(
            "/api/operations/orders?status=Confirmed", cancellationToken);

        Assert.NotNull(confirmed);
        Assert.Empty(confirmed);

        var lookedUp = await client.GetFromJsonAsync<OrderSummary[]>(
            $"/api/operations/orders?orderNumber={first.OrderNumber}", cancellationToken);

        Assert.NotNull(lookedUp);
        Assert.Equal([first.OrderId], lookedUp.Select(order => order.OrderId));

        var detail = await client.GetFromJsonAsync<OrderDetail>(
            $"/api/operations/orders/{first.OrderId:D}", cancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(first.OrderNumber, detail.OrderNumber);
        Assert.Equal("Pending", detail.Status);
        Assert.NotNull(detail.Placement);
        Assert.Equal("AwaitingInventory", detail.Placement.State);
        Assert.Null(detail.Placement.PaymentAuthorisationRequestId);
        Assert.Null(detail.Payment);

        var line = Assert.Single(detail.Lines);
        Assert.Equal("BIKE-001", line.SkuCode);
        Assert.Equal("AwaitingReservation", line.ReservationState);
        Assert.NotNull(line.ReservationRequestId);
        Assert.Null(line.ReservationId);

        using var missingResponse = await client.GetAsync(
            $"/api/operations/orders/{Guid.NewGuid():D}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task ReadsFulfilmentThroughOperationsBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var fulfilmentOptions = new DbContextOptionsBuilder<FulfilmentDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var confirmedAtUtc = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

        await using (var fulfilment = new FulfilmentDbContext(fulfilmentOptions))
        {
            await fulfilment.Database.MigrateAsync(cancellationToken);

            await new CreateFulfilmentHandler(new EfFulfilmentRequestRepository(fulfilment)).HandleAsync(
                new CreateFulfilmentCommand(
                    orderId, Guid.NewGuid(), "SW-00100077",
                    [new CreateFulfilmentLine(lineId, "BIKE-001", "Road Bike", 1)], confirmedAtUtc),
                cancellationToken);

            await fulfilment.SaveChangesAsync(cancellationToken);
        }

        using var application = CreateApplication(postgres.GetConnectionString(), "Development");
        using var client = application.CreateClient();

        var view = await client.GetFromJsonAsync<FulfilmentView>(
            $"/api/operations/orders/{orderId:D}/fulfilment", cancellationToken);

        Assert.NotNull(view);
        Assert.Equal(orderId, view.OrderId);
        Assert.Equal("Pending", view.Status);
        Assert.Equal(confirmedAtUtc, view.ConfirmedAtUtc);
        Assert.Equal("NotRequested", view.Capture.Status);
        Assert.Null(view.Capture.RequestId);

        var line = Assert.Single(view.Lines);
        Assert.Equal(lineId, line.OrderLineId);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(0, line.PreparedQuantity);
        Assert.Empty(view.Shipments);
        Assert.Empty(view.Cancellations);

        // Orders that Fulfilment has not accepted yet are a normal 404.
        using var missingResponse = await client.GetAsync(
            $"/api/operations/orders/{Guid.NewGuid():D}/fulfilment", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task ReadsReturnsThroughOperationsBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = await StartPostgresAsync(cancellationToken);
        await ApplyMigrationsAsync(postgres.GetConnectionString(), cancellationToken);

        var returnsOptions = new DbContextOptionsBuilder<ReturnsDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using (var returns = new ReturnsDbContext(returnsOptions))
        {
            await returns.Database.MigrateAsync(cancellationToken);
        }

        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO returns.return_eligibility (order_id, order_number) VALUES (@order_id, 'SW-00100095');
                INSERT INTO returns.return_eligibility_lines
                    (order_id, order_line_id, delivered_quantity, authorised_return_quantity, returned_quantity)
                VALUES (@order_id, @line_id, 1, 1, 0);
                """,
                connection);

            command.Parameters.AddWithValue("order_id", orderId);
            command.Parameters.AddWithValue("line_id", lineId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using var application = CreateApplication(postgres.GetConnectionString(), "Development");
        using var client = application.CreateClient();

        var view = await client.GetFromJsonAsync<ReturnsView>(
            $"/api/operations/orders/{orderId:D}/returns", cancellationToken);

        Assert.NotNull(view);
        Assert.Equal(orderId, view.OrderId);
        var line = Assert.Single(view.Lines);
        Assert.Equal(lineId, line.OrderLineId);
        Assert.Equal(1, line.DeliveredQuantity);
        Assert.Equal(1, line.AuthorisedReturnQuantity);
        Assert.Equal(0, line.ReturnedQuantity);
        Assert.Empty(view.Requests);

        // Orders with no delivered shipment have nothing in Returns yet.
        using var missingResponse = await client.GetAsync(
            $"/api/operations/orders/{Guid.NewGuid():D}/returns", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Theory]
    [InlineData("status=Shipped", "status")]
    [InlineData("status=1", "status")]
    [InlineData("status=Pending,Failed", "status")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=101", "limit")]
    public async Task RejectsInvalidListQueries(string query, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Validation happens before any query, so no database is needed.
        using var application = CreateApplication(UnusedConnectionString, "Development");
        using var client = application.CreateClient();

        using var response = await client.GetAsync($"/api/operations/orders?{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains($"\"{field}\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DoesNotExposeOperationsEndpointsOutsideDevelopment(string environment)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var application = CreateApplication(UnusedConnectionString, environment);
        using var client = application.CreateClient();

        using var listResponse = await client.GetAsync("/api/operations/orders", cancellationToken);
        using var detailResponse = await client.GetAsync(
            $"/api/operations/orders/{Guid.NewGuid():D}", cancellationToken);
        using var fulfilmentResponse = await client.GetAsync(
            $"/api/operations/orders/{Guid.NewGuid():D}/fulfilment", cancellationToken);
        using var returnsResponse = await client.GetAsync(
            $"/api/operations/orders/{Guid.NewGuid():D}/returns", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fulfilmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, returnsResponse.StatusCode);
    }

    private const string UnusedConnectionString =
        "Host=localhost;Port=1;Database=unused;Username=unused;Password=unused";

    private static async Task<CreatedOrder> CreateOrderAsync(
        HttpClient client, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
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
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreatedOrder>(cancellationToken);

        Assert.NotNull(created);

        return created;
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_operations_api_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        return postgres;
    }

    private static WebApplicationFactory<Program> CreateApplication(string connectionString, string environment)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<OrderingDatabaseOptions>(
                        options => options.ConnectionString = connectionString);
                    services.PostConfigure<FulfilmentDatabaseOptions>(
                        options => options.ConnectionString = connectionString);
                    services.PostConfigure<ReturnsDatabaseOptions>(
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

    private sealed record CreatedOrder(Guid OrderId, string OrderNumber);

    private sealed record OrderSummary(
        Guid OrderId, string OrderNumber, string Status, string? PlacementState, decimal TotalAmount,
        string Currency, int LineCount, DateTimeOffset CreatedAtUtc, DateTimeOffset? PlacementUpdatedAtUtc);

    private sealed record OrderDetail(
        Guid OrderId, string OrderNumber, string Status, decimal TotalAmount, string Currency,
        DateTimeOffset CreatedAtUtc, Placement? Placement, Payment? Payment, IReadOnlyList<OrderLine> Lines);

    private sealed record Payment(
        string Outcome, Guid RequestId, Guid? PaymentId, decimal Amount, string Currency,
        string? ProviderReference, string? Reason, int? ReconciliationAttemptCount,
        DateTimeOffset OutcomeAtUtc, DateTimeOffset RecordedAtUtc);

    private sealed record Placement(
        string State, DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc, Guid? PaymentAuthorisationRequestId);

    private sealed record OrderLine(
        Guid OrderLineId, string SkuCode, string ProductName, int Quantity, decimal UnitPriceAmount,
        decimal LineTotalAmount, string Currency, string? ReservationState, Guid? ReservationRequestId,
        Guid? ReservationId, DateTimeOffset? ReservationExpiresAtUtc);

    private sealed record FulfilmentView(
        Guid OrderId, string Status, DateTimeOffset ConfirmedAtUtc, Guid PaymentId, FulfilmentCapture Capture,
        IReadOnlyList<FulfilmentLine> Lines, IReadOnlyList<FulfilmentShipment> Shipments,
        IReadOnlyList<FulfilmentCancellation> Cancellations);

    private sealed record FulfilmentCapture(
        string Status, Guid? RequestId, DateTimeOffset? RequestedAtUtc, string? ProviderReference,
        DateTimeOffset? ResolvedAtUtc);

    private sealed record FulfilmentLine(
        Guid OrderLineId, string SkuCode, string ProductName, int Quantity, int PreparedQuantity,
        int CancelledQuantity);

    private sealed record FulfilmentShipment(
        Guid ShipmentId, int ItemQuantity, DateTimeOffset CreatedAtUtc, DateTimeOffset? DispatchedAtUtc,
        DateTimeOffset? DeliveredAtUtc);

    private sealed record FulfilmentCancellation(
        Guid CancellationId, string Outcome, string? RejectionReason, int Quantity, DateTimeOffset RequestedAtUtc);

    private sealed record ReturnsView(
        Guid OrderId, IReadOnlyList<ReturnLine> Lines, IReadOnlyList<ReturnRequest> Requests);

    private sealed record ReturnLine(
        Guid OrderLineId, int DeliveredQuantity, int AuthorisedReturnQuantity, int ReturnedQuantity);

    private sealed record ReturnRequest(
        Guid ReturnRequestId, string Status, int Quantity, DateTimeOffset RequestedAtUtc,
        IReadOnlyList<ReturnReceipt> Receipts);

    private sealed record ReturnReceipt(
        Guid ReturnReceiptId, int Quantity, DateTimeOffset ReceivedAtUtc, ReturnRefund? Refund);

    private sealed record ReturnRefund(
        string Status, decimal Amount, string Currency, string? ProviderReference, DateTimeOffset RequestedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
