using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;
using Switchyard.Ordering.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class OrderingInboundMessageConsumer :
    IIntegrationMessageConsumer
{
    private const string ConsumerName = "ordering-placement";

    private readonly IDbContextFactory<OrderingDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<
        string,
        IOrderingInboundMessageRoute> _routes;

    public OrderingInboundMessageConsumer(
        IDbContextFactory<OrderingDbContext> dbContextFactory,
        TimeProvider timeProvider,
        IEnumerable<IOrderingInboundMessageRoute> routes)
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
            if (string.IsNullOrWhiteSpace(route.MessageType))
            {
                throw new InvalidOperationException(
                    "Ordering inbound message routes require a message type.");
            }
        }

        _routes =
            routeList.ToDictionary(
                route => route.MessageType.Trim(),
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
            new EfOrderingInboxMessageProcessor(
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
