using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Data.Sqlite;

public static class SqliteBuilderExtensions
{
    public static FluentCmsBuilder UseSqlite(this FluentCmsBuilder cms, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(cms);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return cms.UseDataProvider(new SqliteDataProvider(connectionString));
    }
}
