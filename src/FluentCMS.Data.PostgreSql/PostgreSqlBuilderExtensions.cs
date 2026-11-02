using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Data.PostgreSql;

public static class PostgreSqlBuilderExtensions
{
    public static FluentCmsBuilder UsePostgreSql(this FluentCmsBuilder cms, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(cms);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return cms.UseDataProvider(new PostgreSqlDataProvider(connectionString));
    }
}
