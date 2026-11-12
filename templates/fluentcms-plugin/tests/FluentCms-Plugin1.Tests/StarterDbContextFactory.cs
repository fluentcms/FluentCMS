using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCms_Plugin1.Tests;

// Used only by `dotnet ef migrations add`, which never opens these databases. The argument after `--` names the set to
// scaffold into: Sqlite, PostgreSql or SqlServer.
public sealed class StarterDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<StarterPlugin>>
{
    public PluginDbContext<StarterPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<StarterPlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new StarterModel());
}
