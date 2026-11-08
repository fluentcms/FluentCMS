using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Data.SqlServer;

public static class SqlServerBuilderExtensions
{
    public static FluentCmsBuilder UseSqlServer(this FluentCmsBuilder cms, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(cms);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return cms.UseDataProvider(new SqlServerDataProvider(connectionString));
    }
}
