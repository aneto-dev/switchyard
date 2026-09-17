using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    private const string ConnectionStringEnvironmentVariable = "SWITCHYARD_ORDERING_CONNECTION_STRING";

    public OrderingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} before running Ordering EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderingDbContext(options);
    }
}
