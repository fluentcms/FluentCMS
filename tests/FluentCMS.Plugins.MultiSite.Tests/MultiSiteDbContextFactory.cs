using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.MultiSite.Tests;

// Used only by `dotnet ef migrations add` for the multi-site stream, which never opens these databases. The argument after
// `--` names the set to scaffold into. See docs/plugin-migrations.md.
public sealed class MultiSiteDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<MultiSitePlugin>>
{
    public PluginDbContext<MultiSitePlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<MultiSitePlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new MultiSiteModel());
}
