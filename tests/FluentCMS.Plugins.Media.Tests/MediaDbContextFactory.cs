using FluentCMS.Core.Plugins;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.Media.Tests;

// Used only by `dotnet ef migrations add` for the media stream, which never opens these databases. The argument after `--`
// names the set to scaffold into. See docs/plugin-migrations.md.
public sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<MediaPlugin>>
{
    public PluginDbContext<MediaPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<MediaPlugin>(
            args,
            [
                new SqliteDataProvider("Data Source=:memory:"),
                new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
                new SqlServerDataProvider("Server=localhost;Database=never_opened"),
            ],
            new MediaModel());
}
