using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Switchyard.Ordering.Application.Ports;
using Switchyard.Ordering.Domain.Orders;

namespace Switchyard.Ordering.Infrastructure.Persistence;

public sealed class PostgresOrderNumberGenerator : IOrderNumberGenerator
{
    private readonly OrderingDbContext _dbContext;

    public PostgresOrderNumberGenerator(OrderingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OrderNumber> NextAsync(CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var closeAfterUse = connection.State != ConnectionState.Open;

        if (closeAfterUse)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "select nextval('ordering.order_number_sequence')";

            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result is null || result is DBNull)
            {
                throw new InvalidOperationException("PostgreSQL did not return the next order number sequence value.");
            }

            var value = Convert.ToInt64(result, CultureInfo.InvariantCulture);
            return new OrderNumber($"SW-{value:D8}");
        }
        finally
        {
            if (closeAfterUse)
            {
                await connection.CloseAsync();
            }
        }
    }
}
