using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Switchyard.Returns.Infrastructure.Persistence;

public sealed class ReturnsDbContextFactory :
    IDesignTimeDbContextFactory<ReturnsDbContext>
{
    public ReturnsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "SWITCHYARD_RETURNS_CONNECTION_STRING") ??
            "Host=localhost;Port=5432;Database=switchyard;Username=switchyard;Password=switchyard-local-only";

        var options =
            new DbContextOptionsBuilder<ReturnsDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new ReturnsDbContext(options);
    }
}
