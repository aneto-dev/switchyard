using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Switchyard.Api.Health;
using Switchyard.Api.Ordering;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
                .AddCheck<OrderingDatabaseHealthCheck>("ordering-database", tags: HealthCheckTags.Ready);

builder.Services.AddOptions<OrderingDatabaseOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.ConnectionString = configuration.GetConnectionString(OrderingDatabaseOptions.ConnectionStringName)
                                               ?? string.Empty;
                })
                .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                          "Connection string 'Ordering' is required.")
                .ValidateOnStart();

builder.Services.AddDbContext<OrderingDbContext>((serviceProvider, options) =>
{
    var databaseOptions = serviceProvider.GetRequiredService<IOptions<OrderingDatabaseOptions>>().Value;

    options.UseNpgsql(databaseOptions.ConnectionString);
});

builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IOrderRequestRepository, EfOrderRequestRepository>();
builder.Services.AddScoped<IOrderingUnitOfWork, EfOrderingUnitOfWork>();
builder.Services.AddScoped<IOrderNumberGenerator, PostgresOrderNumberGenerator>();
builder.Services.AddScoped<CreatePendingOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Switchyard.Api",
    status = "order-management-core",
    version = "0.2.0-dev"
}));

app.MapOrderingEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponseAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});

app.Run();

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString().ToLowerInvariant()
    }));
}

internal static class HealthCheckTags
{
    internal static readonly string[] Ready = ["ready"];
}

public partial class Program;
