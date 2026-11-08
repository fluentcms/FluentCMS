using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Plugins.Sample;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.Tests;

// Used only by `dotnet ef migrations add` for the sample stream, which never opens these databases. The argument after `--`
// names the set to scaffold into. See docs/plugin-migrations.md.
public sealed class SampleDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<SamplePlugin>>
{
    public PluginDbContext<SamplePlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<SamplePlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new SampleModel());
}
