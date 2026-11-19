namespace Switchyard.Api.Operations;

public sealed class FulfilmentDatabaseOptions
{
    public const string ConnectionStringName = "Fulfilment";

    public string ConnectionString { get; set; } = string.Empty;
}
