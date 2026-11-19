using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Switchyard.Api.Health;
using Switchyard.Api.Operations;
using Switchyard.Fulfilment.Application.Operations;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.Api.Ordering;
using Switchyard.Messaging;
using Switchyard.Ordering.Application.Operations;
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

// Fulfilment is only read by the development-only operations endpoints, so its connection
// string is resolved on first use rather than validated at start-up.
builder.Services.AddOptions<FulfilmentDatabaseOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.ConnectionString = configuration.GetConnectionString(FulfilmentDatabaseOptions.ConnectionStringName)
                                               ?? string.Empty;
                });

builder.Services.AddDbContext<FulfilmentDbContext>((serviceProvider, options) =>
{
    var databaseOptions = serviceProvider.GetRequiredService<IOptions<FulfilmentDatabaseOptions>>().Value;

    if (string.IsNullOrWhiteSpace(databaseOptions.ConnectionString))
    {
        throw new InvalidOperationException("Connection string 'Fulfilment' is required for operations endpoints.");
    }

    options.UseNpgsql(databaseOptions.ConnectionString);
});

builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IOrderRequestRepository, EfOrderRequestRepository>();
builder.Services.AddScoped<IOrderPlacementProcessRepository, EfOrderPlacementProcessRepository>();
builder.Services.AddScoped<IOrderingUnitOfWork, EfOrderingUnitOfWork>();
builder.Services.AddScoped<IOutboxWriter, EfOrderingOutboxStore>();
builder.Services.AddScoped<IOrderNumberGenerator, PostgresOrderNumberGenerator>();
builder.Services.AddScoped<CreatePendingOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();
builder.Services.AddScoped<IOperationsOrderQueries, EfOperationsOrderQueries>();
builder.Services.AddScoped<IOperationsFulfilmentQueries, EfOperationsFulfilmentQueries>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Switchyard.Api",
    status = "reliable-messaging",
    version = "0.5.0-dev"
}));

app.MapOrderingEndpoints();

// Operations endpoints have no authentication or role checks yet (ADR-0004 operations roles
// arrive with the security milestone), so they are only mapped for local development.
if (app.Environment.IsDevelopment())
{
    app.MapOperationsOrderEndpoints();
}

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
