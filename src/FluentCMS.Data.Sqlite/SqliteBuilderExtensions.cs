using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Data.Sqlite;

public static class SqliteBuilderExtensions
{
    // writeAheadLog false leaves a database file in the journal mode it has, with SQLite's defaults for every connection.
    public static FluentCmsBuilder UseSqlite(this FluentCmsBuilder cms, string? connectionString, bool writeAheadLog = true)
    {
        ArgumentNullException.ThrowIfNull(cms);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return cms.UseDataProvider(new SqliteDataProvider(connectionString, writeAheadLog));
    }
}
