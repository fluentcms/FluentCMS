using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.Search.Tests;

// Used only by `dotnet ef migrations add` for the search stream, which never opens these databases. The argument after
// `--` names the set to scaffold into. See docs/plugin-migrations.md.
public sealed class SearchDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<SearchPlugin>>
{
    public PluginDbContext<SearchPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<SearchPlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new SearchModel());
}
