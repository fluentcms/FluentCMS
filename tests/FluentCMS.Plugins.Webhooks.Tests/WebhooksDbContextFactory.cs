using FluentCMS.Core.Plugins;
using FluentCMS.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Plugins.Webhooks.Tests;

// Used only by `dotnet ef migrations add` for the webhooks stream, which never opens this database. See
// docs/plugin-migrations.md.
public sealed class WebhooksDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext<WebhooksPlugin>>
{
    public PluginDbContext<WebhooksPlugin> CreateDbContext(string[] args) =>
        PluginDbContextDesignTime.Create<WebhooksPlugin>(new SqliteDataProvider("Data Source=:memory:"), new WebhookModel());
}
