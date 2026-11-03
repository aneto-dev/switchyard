using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Switchyard.Messaging;
using Switchyard.Messaging.ServiceBus;
using Switchyard.Ordering.Infrastructure.Persistence;
using Switchyard.Payments.Application.Ports;
using Switchyard.Payments.Infrastructure.Persistence;
using Switchyard.Payments.Infrastructure.ProviderSimulation;
using Switchyard.Worker;

var builder = Host.CreateApplicationBuilder(args);

var orderingConnectionString = GetRequiredSetting(
    builder.Configuration["SWITCHYARD_ORDERING_CONNECTION_STRING"],
    "SWITCHYARD_ORDERING_CONNECTION_STRING");

var inventoryConnectionString = GetRequiredSetting(
    builder.Configuration["SWITCHYARD_INVENTORY_CONNECTION_STRING"],
    "SWITCHYARD_INVENTORY_CONNECTION_STRING");

var paymentsConnectionString = GetRequiredSetting(
    builder.Configuration["SWITCHYARD_PAYMENTS_CONNECTION_STRING"],
    "SWITCHYARD_PAYMENTS_CONNECTION_STRING");

var serviceBusOptions = new ServiceBusTransportOptions(
    builder.Configuration["SWITCHYARD_SERVICEBUS_TOPIC"] ?? "switchyard-events",
    builder.Configuration["SWITCHYARD_SERVICEBUS_CONNECTION_STRING"],
    builder.Configuration["SWITCHYARD_SERVICEBUS_NAMESPACE"]);

var orderingReceiveOptions = new ServiceBusReceiveOptions(
    builder.Configuration["SWITCHYARD_SERVICEBUS_SUBSCRIPTION"] ?? "ordering");

var inventoryReceiveOptions = new ServiceBusReceiveOptions(
    builder.Configuration["SWITCHYARD_INVENTORY_SERVICEBUS_SUBSCRIPTION"] ?? "inventory");

var inventoryReleaseReceiveOptions = new ServiceBusReceiveOptions(
    builder.Configuration["SWITCHYARD_INVENTORY_RELEASE_SERVICEBUS_SUBSCRIPTION"] ?? "inventory-release");

var paymentsReceiveOptions = new ServiceBusReceiveOptions(
    builder.Configuration["SWITCHYARD_PAYMENTS_SERVICEBUS_SUBSCRIPTION"] ?? "payments");

var orderingOutboxOptions = new OrderingOutboxWorkerOptions(
    GetPositiveInt(
        builder.Configuration["SWITCHYARD_OUTBOX_BATCH_SIZE"],
        50,
        "SWITCHYARD_OUTBOX_BATCH_SIZE"),
    TimeSpan.FromSeconds(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_OUTBOX_LEASE_SECONDS"],
            60,
            "SWITCHYARD_OUTBOX_LEASE_SECONDS")),
    TimeSpan.FromSeconds(
        GetNonNegativeInt(
            builder.Configuration["SWITCHYARD_OUTBOX_RETRY_SECONDS"],
            30,
            "SWITCHYARD_OUTBOX_RETRY_SECONDS")),
    TimeSpan.FromMilliseconds(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_OUTBOX_POLL_MILLISECONDS"],
            500,
            "SWITCHYARD_OUTBOX_POLL_MILLISECONDS")));

var inventoryOutboxOptions = new InventoryOutboxWorkerOptions(
    GetPositiveInt(
        builder.Configuration["SWITCHYARD_INVENTORY_OUTBOX_BATCH_SIZE"],
        50,
        "SWITCHYARD_INVENTORY_OUTBOX_BATCH_SIZE"),
    TimeSpan.FromSeconds(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_INVENTORY_OUTBOX_LEASE_SECONDS"],
            60,
            "SWITCHYARD_INVENTORY_OUTBOX_LEASE_SECONDS")),
    TimeSpan.FromSeconds(
        GetNonNegativeInt(
            builder.Configuration["SWITCHYARD_INVENTORY_OUTBOX_RETRY_SECONDS"],
            30,
            "SWITCHYARD_INVENTORY_OUTBOX_RETRY_SECONDS")),
    TimeSpan.FromMilliseconds(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_INVENTORY_OUTBOX_POLL_MILLISECONDS"],
            500,
            "SWITCHYARD_INVENTORY_OUTBOX_POLL_MILLISECONDS")));

var paymentsOutboxOptions = new PaymentsOutboxWorkerOptions(
    GetPositiveInt(
        builder.Configuration["SWITCHYARD_PAYMENTS_OUTBOX_BATCH_SIZE"],
        50,
        "SWITCHYARD_PAYMENTS_OUTBOX_BATCH_SIZE"),
    TimeSpan.FromSeconds(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_PAYMENTS_OUTBOX_LEASE_SECONDS"],
            60,
            "SWITCHYARD_PAYMENTS_OUTBOX_LEASE_SECONDS")),
    TimeSpan.FromSeconds(
        GetNonNegativeInt(
            builder.Configuration["SWITCHYARD_PAYMENTS_OUTBOX_RETRY_SECONDS"],
            30,
            "SWITCHYARD_PAYMENTS_OUTBOX_RETRY_SECONDS")),
    TimeSpan.FromMilliseconds(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_PAYMENTS_OUTBOX_POLL_MILLISECONDS"],
            500,
            "SWITCHYARD_PAYMENTS_OUTBOX_POLL_MILLISECONDS")));

var paymentsReconciliationOptions =
    new PaymentsAuthorisationReconciliationOptions(
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_PAYMENTS_RECONCILIATION_BATCH_SIZE"],
            25,
            "SWITCHYARD_PAYMENTS_RECONCILIATION_BATCH_SIZE"),
        GetPositiveInt(
            builder.Configuration["SWITCHYARD_PAYMENTS_RECONCILIATION_MAX_ATTEMPTS"],
            5,
            "SWITCHYARD_PAYMENTS_RECONCILIATION_MAX_ATTEMPTS"),
        TimeSpan.FromSeconds(
            GetNonNegativeInt(
                builder.Configuration["SWITCHYARD_PAYMENTS_RECONCILIATION_RETRY_SECONDS"],
                30,
                "SWITCHYARD_PAYMENTS_RECONCILIATION_RETRY_SECONDS")),
        TimeSpan.FromMilliseconds(
            GetPositiveInt(
                builder.Configuration["SWITCHYARD_PAYMENTS_RECONCILIATION_POLL_MILLISECONDS"],
                1000,
                "SWITCHYARD_PAYMENTS_RECONCILIATION_POLL_MILLISECONDS")));

var paymentAuthorisationScenario =
    GetEnumSetting(
        builder.Configuration["SWITCHYARD_PAYMENT_AUTHORISATION_SCENARIO"],
        SimulatedPaymentAuthorisationScenario.Authorise,
        "SWITCHYARD_PAYMENT_AUTHORISATION_SCENARIO");

var paymentReconciliationScenario =
    GetEnumSetting(
        builder.Configuration["SWITCHYARD_PAYMENT_RECONCILIATION_SCENARIO"],
        SimulatedPaymentReconciliationScenario.Authorise,
        "SWITCHYARD_PAYMENT_RECONCILIATION_SCENARIO");

var reservationPolicy =
    new InventoryReservationPolicy(
        TimeSpan.FromMinutes(
            GetPositiveInt(
                builder.Configuration["SWITCHYARD_INVENTORY_RESERVATION_MINUTES"],
                15,
                "SWITCHYARD_INVENTORY_RESERVATION_MINUTES")));

builder.Services.AddDbContextFactory<OrderingDbContext>(
    options =>
        options.UseNpgsql(
            orderingConnectionString));

builder.Services.AddDbContextFactory<InventoryDbContext>(
    options =>
        options.UseNpgsql(
            inventoryConnectionString));

builder.Services.AddDbContextFactory<PaymentsDbContext>(
    options =>
        options.UseNpgsql(
            paymentsConnectionString));

builder.Services.AddSingleton(serviceBusOptions);
builder.Services.AddSingleton(orderingOutboxOptions);
builder.Services.AddSingleton(inventoryOutboxOptions);
builder.Services.AddSingleton(paymentsOutboxOptions);
builder.Services.AddSingleton(paymentsReconciliationOptions);
builder.Services.AddSingleton(reservationPolicy);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(
    new SimulatedPaymentAuthorisationProvider(
        paymentAuthorisationScenario,
        paymentReconciliationScenario));

builder.Services.AddSingleton<IPaymentAuthorisationProvider>(
    provider =>
        provider.GetRequiredService<
            SimulatedPaymentAuthorisationProvider>());

builder.Services.AddSingleton<IPaymentAuthorisationReconciliationProvider>(
    provider =>
        provider.GetRequiredService<
            SimulatedPaymentAuthorisationProvider>());

builder.Services.AddSingleton(
    _ => ServiceBusClientFactory.Create(
        serviceBusOptions));

builder.Services.AddSingleton(
    provider =>
        provider.GetRequiredService<ServiceBusClient>()
                .CreateSender(
                    serviceBusOptions.TopicName));

builder.Services.AddSingleton<
    IMessageTransport,
    ServiceBusMessageTransport>();

builder.Services.AddSingleton<OrderingInboundMessageConsumer>();
builder.Services.AddSingleton<InventoryInboundMessageConsumer>();
builder.Services.AddSingleton<PaymentsInboundMessageConsumer>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    InventoryReservedInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    InventoryRejectedInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    InventoryReleasedInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    InventoryExpiredInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    PaymentAuthorisedInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    PaymentAuthorisationFailedInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    PaymentAuthorisationIndeterminateInboundMessageRoute>();
builder.Services.AddSingleton<
    IOrderingInboundMessageRoute,
    PaymentAuthorisationNeedsAttentionInboundMessageRoute>();
builder.Services.AddSingleton<
    IInventoryInboundMessageRoute,
    ReserveInventoryInboundMessageRoute>();
builder.Services.AddSingleton<
    IInventoryInboundMessageRoute,
    ReleaseInventoryInboundMessageRoute>();

builder.Services.AddSingleton<ServiceBusSubscriptionEndpoint>(
    provider =>
        CreateEndpoint(
            "ordering",
            provider,
            serviceBusOptions,
            orderingReceiveOptions,
            provider.GetRequiredService<OrderingInboundMessageConsumer>()));

builder.Services.AddSingleton<ServiceBusSubscriptionEndpoint>(
    provider =>
        CreateEndpoint(
            "inventory",
            provider,
            serviceBusOptions,
            inventoryReceiveOptions,
            provider.GetRequiredService<InventoryInboundMessageConsumer>()));

builder.Services.AddSingleton<ServiceBusSubscriptionEndpoint>(
    provider =>
        CreateEndpoint(
            "inventory-release",
            provider,
            serviceBusOptions,
            inventoryReleaseReceiveOptions,
            provider.GetRequiredService<InventoryInboundMessageConsumer>()));

builder.Services.AddSingleton<ServiceBusSubscriptionEndpoint>(
    provider =>
        CreateEndpoint(
            "payments",
            provider,
            serviceBusOptions,
            paymentsReceiveOptions,
            provider.GetRequiredService<PaymentsInboundMessageConsumer>()));

builder.Services.AddSingleton<
    IOrderingOutboxDispatchCycle,
    OrderingOutboxDispatchCycle>();

builder.Services.AddSingleton<
    IInventoryOutboxDispatchCycle,
    InventoryOutboxDispatchCycle>();

builder.Services.AddSingleton<
    IPaymentsOutboxDispatchCycle,
    PaymentsOutboxDispatchCycle>();

builder.Services.AddSingleton<
    IPaymentsAuthorisationReconciliationCycle,
    PaymentsAuthorisationReconciliationCycle>();

builder.Services.AddHostedService<OrderingOutboxDispatchWorker>();
builder.Services.AddHostedService<InventoryOutboxDispatchWorker>();
builder.Services.AddHostedService<PaymentsOutboxDispatchWorker>();
builder.Services.AddHostedService<PaymentsAuthorisationReconciliationWorker>();
builder.Services.AddHostedService<ServiceBusSubscriptionWorker>();

await builder.Build().RunAsync();

static ServiceBusSubscriptionEndpoint CreateEndpoint(
    string name,
    IServiceProvider provider,
    ServiceBusTransportOptions transportOptions,
    ServiceBusReceiveOptions receiveOptions,
    IIntegrationMessageConsumer consumer)
{
    var processor =
        provider.GetRequiredService<ServiceBusClient>()
                .CreateProcessor(
                    transportOptions.TopicName,
                    receiveOptions.SubscriptionName,
                    new ServiceBusProcessorOptions
                    {
                        AutoCompleteMessages = false,
                        ReceiveMode = ServiceBusReceiveMode.PeekLock,
                        MaxConcurrentCalls =
                            receiveOptions.MaxConcurrentCalls,
                        MaxAutoLockRenewalDuration =
                            receiveOptions.MaxAutoLockRenewalDuration
                    });

    var messageProcessor =
        new ServiceBusInboundMessageProcessor(
            consumer,
            receiveOptions,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<
                ILogger<ServiceBusInboundMessageProcessor>>());

    return new ServiceBusSubscriptionEndpoint(
        name,
        processor,
        messageProcessor);
}

static string GetRequiredSetting(
    string? value,
    string name)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Set {name} before starting Switchyard.Worker.");
    }

    return value.Trim();
}

static int GetPositiveInt(
    string? rawValue,
    int defaultValue,
    string name)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return defaultValue;
    }

    if (!int.TryParse(rawValue, out var value) ||
        value <= 0)
    {
        throw new InvalidOperationException(
            $"{name} must be a positive integer.");
    }

    return value;
}

static int GetNonNegativeInt(
    string? rawValue,
    int defaultValue,
    string name)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return defaultValue;
    }

    if (!int.TryParse(rawValue, out var value) ||
        value < 0)
    {
        throw new InvalidOperationException(
            $"{name} must be a non-negative integer.");
    }

    return value;
}

static TEnum GetEnumSetting<TEnum>(
    string? rawValue,
    TEnum defaultValue,
    string name)
    where TEnum : struct, Enum
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return defaultValue;
    }

    if (!Enum.TryParse<TEnum>(
            rawValue.Trim(),
            ignoreCase: true,
            out var value) ||
        !Enum.IsDefined(value))
    {
        throw new InvalidOperationException(
            $"{name} contains an unsupported value.");
    }

    return value;
}
