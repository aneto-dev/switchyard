namespace Switchyard.Api.Operations;

public sealed class ReturnsDatabaseOptions
{
    public const string ConnectionStringName = "Returns";

    public string ConnectionString { get; set; } = string.Empty;
}
