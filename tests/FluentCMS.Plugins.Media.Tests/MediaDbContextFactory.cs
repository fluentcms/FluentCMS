using FluentCMS.Core.Plugins;
using FluentCMS.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.Media.Tests;

// Used only by `dotnet ef migrations add` for the media stream, which never opens this database. See
// docs/plugin-migrations.md.
public sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<MediaPlugin>>
{
    public PluginDbContext<MediaPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<MediaPlugin>(new SqliteDataProvider("Data Source=:memory:"), new MediaModel());
}
