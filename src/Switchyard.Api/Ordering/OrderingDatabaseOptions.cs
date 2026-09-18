namespace Switchyard.Api.Ordering;

public sealed class OrderingDatabaseOptions
{
    public const string ConnectionStringName = "Ordering";

    public string ConnectionString { get; set; } = string.Empty;
}
