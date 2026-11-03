using Microsoft.EntityFrameworkCore;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Switchyard.Messaging;

namespace Switchyard.Worker;

public sealed class FulfilmentInboundMessageConsumer :
    IIntegrationMessageConsumer
{
    private const string ConsumerName =
        "fulfilment-confirmed-orders";

    private readonly IDbContextFactory<FulfilmentDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<
        string,
        IFulfilmentInboundMessageRoute> _routes;

    public FulfilmentInboundMessageConsumer(
        IDbContextFactory<FulfilmentDbContext> dbContextFactory,
        TimeProvider timeProvider,
        IEnumerable<IFulfilmentInboundMessageRoute> routes)
    {
        _dbContextFactory =
            dbContextFactory ??
            throw new ArgumentNullException(nameof(dbContextFactory));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));

        ArgumentNullException.ThrowIfNull(routes);

        var routeList =
            routes.ToArray();

        foreach (var route in routeList)
        {
            if (string.IsNullOrWhiteSpace(
                    route.MessageType))
            {
                throw new InvalidOperationException(
                    "Fulfilment inbound message routes require a message type.");
            }
        }

        _routes =
            routeList.ToDictionary(
                route =>
                    route.MessageType.Trim(),
                StringComparer.Ordinal);
    }

    public async Task ConsumeAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_routes.TryGetValue(
                message.MessageType,
                out var route))
        {
            throw new UnsupportedIntegrationMessageException(
                message.MessageType);
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var inbox =
            new EfFulfilmentInboxMessageProcessor(
                dbContext,
                _timeProvider);

        await inbox.ProcessAsync(
            ConsumerName,
            message,
            token =>
                route.HandleAsync(
                    dbContext,
                    message,
                    token),
            cancellationToken);
    }
}
