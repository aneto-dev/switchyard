using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Switchyard.Fulfilment.Infrastructure.Persistence;

public sealed class FulfilmentDbContextFactory :
    IDesignTimeDbContextFactory<FulfilmentDbContext>
{
    private const string ConnectionStringEnvironmentVariable =
        "SWITCHYARD_FULFILMENT_CONNECTION_STRING";

    public FulfilmentDbContext CreateDbContext(
        string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} before running Fulfilment EF Core design-time commands.");
        }

        var options =
            new DbContextOptionsBuilder<FulfilmentDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new FulfilmentDbContext(options);
    }
}
