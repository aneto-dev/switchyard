using System.Globalization;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Switchyard.IntegrationTests;

public sealed class PostgreSqlContainerTests
{
    [Fact]
    public async Task PostgreSql18ContainerAcceptsRealConnection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("switchyard_test")
            .WithUsername("switchyard")
            .WithPassword("switchyard-test-only")
            .Build();

        await postgres.StartAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("select 1", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
    }
}
