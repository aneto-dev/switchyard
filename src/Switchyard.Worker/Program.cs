using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Switchyard.Messaging;
using Switchyard.Messaging.ServiceBus;
using Switchyard.Ordering.Infrastructure.Persistence;
using Switchyard.Worker;

var builder = Host.CreateApplicationBuilder(args);

var orderingConnectionString = GetRequiredSetting(
    builder.Configuration["SWITCHYARD_ORDERING_CONNECTION_STRING"],
    "SWITCHYARD_ORDERING_CONNECTION_STRING");

var serviceBusOptions = new ServiceBusTransportOptions(
    builder.Configuration["SWITCHYARD_SERVICEBUS_TOPIC"] ?? "switchyard-events",
    builder.Configuration["SWITCHYARD_SERVICEBUS_CONNECTION_STRING"],
    builder.Configuration["SWITCHYARD_SERVICEBUS_NAMESPACE"]);

var serviceBusReceiveOptions = new ServiceBusReceiveOptions(
    builder.Configuration["SWITCHYARD_SERVICEBUS_SUBSCRIPTION"] ?? "ordering");

var outboxOptions = new OrderingOutboxWorkerOptions(
    GetPositiveInt(builder.Configuration["SWITCHYARD_OUTBOX_BATCH_SIZE"], 50, "SWITCHYARD_OUTBOX_BATCH_SIZE"),
    TimeSpan.FromSeconds(GetPositiveInt(builder.Configuration["SWITCHYARD_OUTBOX_LEASE_SECONDS"], 60, "SWITCHYARD_OUTBOX_LEASE_SECONDS")),
    TimeSpan.FromSeconds(GetNonNegativeInt(builder.Configuration["SWITCHYARD_OUTBOX_RETRY_SECONDS"], 30, "SWITCHYARD_OUTBOX_RETRY_SECONDS")),
    TimeSpan.FromMilliseconds(GetPositiveInt(builder.Configuration["SWITCHYARD_OUTBOX_POLL_MILLISECONDS"], 500, "SWITCHYARD_OUTBOX_POLL_MILLISECONDS")));

builder.Services.AddDbContextFactory<OrderingDbContext>(options => options.UseNpgsql(orderingConnectionString));
builder.Services.AddSingleton(serviceBusOptions);
builder.Services.AddSingleton(serviceBusReceiveOptions);
builder.Services.AddSingleton(_ => ServiceBusClientFactory.Create(serviceBusOptions));
builder.Services.AddSingleton(provider =>
    provider.GetRequiredService<ServiceBusClient>().CreateSender(serviceBusOptions.TopicName));
builder.Services.AddSingleton(provider =>
    provider.GetRequiredService<ServiceBusClient>().CreateProcessor(
        serviceBusOptions.TopicName,
        serviceBusReceiveOptions.SubscriptionName,
        new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            MaxConcurrentCalls = serviceBusReceiveOptions.MaxConcurrentCalls,
            MaxAutoLockRenewalDuration = serviceBusReceiveOptions.MaxAutoLockRenewalDuration
        }));
builder.Services.AddSingleton<IMessageTransport, ServiceBusMessageTransport>();
builder.Services.AddSingleton<IIntegrationMessageConsumer, OrderingInboundMessageConsumer>();
builder.Services.AddSingleton<ServiceBusInboundMessageProcessor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(outboxOptions);
builder.Services.AddSingleton<IOrderingOutboxDispatchCycle, OrderingOutboxDispatchCycle>();
builder.Services.AddHostedService<OrderingOutboxDispatchWorker>();
builder.Services.AddHostedService<ServiceBusSubscriptionWorker>();

await builder.Build().RunAsync();

static string GetRequiredSetting(string? value, string name)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Set {name} before starting Switchyard.Worker.");
    }

    return value.Trim();
}

static int GetPositiveInt(string? rawValue, int defaultValue, string name)
{
    if (string.IsNullOrWhiteSpace(rawValue)) { return defaultValue; }

    if (!int.TryParse(rawValue, out var value) || value <= 0)
    {
        throw new InvalidOperationException($"{name} must be a positive integer.");
    }

    return value;
}

static int GetNonNegativeInt(string? rawValue, int defaultValue, string name)
{
    if (string.IsNullOrWhiteSpace(rawValue)) { return defaultValue; }

    if (!int.TryParse(rawValue, out var value) || value < 0)
    {
        throw new InvalidOperationException($"{name} must be a non-negative integer.");
    }

    return value;
}
