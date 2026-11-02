using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Data.SqlServer;

public static class SqlServerBuilderExtensions
{
    // nativeJson stores JSON in the json type of SQL Server 2025, instead of nvarchar(max).
    public static FluentCmsBuilder UseSqlServer(this FluentCmsBuilder cms, string? connectionString, bool nativeJson = false)
    {
        ArgumentNullException.ThrowIfNull(cms);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return cms.UseDataProvider(new SqlServerDataProvider(connectionString, nativeJson));
    }
}
