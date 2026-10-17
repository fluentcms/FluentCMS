namespace FluentCMS.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    // "sqlite" or "postgresql". Left empty, the provider is inferred from the connection string.
    public string? Provider { get; set; }

    public string ConnectionString { get; set; } = "Data Source=fluentcms.db";

    public string ResolveProvider()
    {
        if (!string.IsNullOrWhiteSpace(Provider))
        {
            return Provider.Trim().ToLowerInvariant();
        }

        return ConnectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProviders.PostgreSql
            : DatabaseProviders.Sqlite;
    }
}

public static class DatabaseProviders
{
    public const string Sqlite = "sqlite";
    public const string PostgreSql = "postgresql";
}
