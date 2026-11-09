using Microsoft.EntityFrameworkCore;
using Switchyard.Messaging;
using Switchyard.Returns.Infrastructure.Persistence;

namespace Switchyard.Worker;

public sealed class ReturnsInboundMessageConsumer : IIntegrationMessageConsumer
{
    private const string ConsumerName = "returns-shipment-deliveries";

    private readonly IDbContextFactory<ReturnsDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, IReturnsInboundMessageRoute> _routes;

    public ReturnsInboundMessageConsumer(
        IDbContextFactory<ReturnsDbContext> dbContextFactory,
        TimeProvider timeProvider,
        IEnumerable<IReturnsInboundMessageRoute> routes)
    {
        _dbContextFactory =
            dbContextFactory ??
            throw new ArgumentNullException(nameof(dbContextFactory));

        _timeProvider =
            timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));

        _routes =
            routes.ToDictionary(
                route => route.MessageType,
                StringComparer.Ordinal);
    }

    public async Task ConsumeAsync(
        IntegrationMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_routes.TryGetValue(message.MessageType, out var route))
        {
            throw new UnsupportedIntegrationMessageException(message.MessageType);
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var inbox =
            new EfReturnsInboxMessageProcessor(
                dbContext,
                _timeProvider);

        await inbox.ProcessAsync(
            ConsumerName,
            message,
            token => route.HandleAsync(dbContext, message, token),
            cancellationToken);
    }
}
