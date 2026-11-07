using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Core.Storage;

// The content migration stream: CmsDbContext with the migrations of the host and their history table.
public static class ContentMigrations
{
    // For the host's IDesignTimeDbContextFactory<CmsDbContext>, which `fluentcms migrations add` looks for.
    public static CmsDbContext CreateContext(IDataProvider provider, SchemaModel schema, Assembly migrationsAssembly)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(migrationsAssembly);

        var options = new DbContextOptionsBuilder<CmsDbContext>();
        Configure(options, provider, schema, migrationsAssembly);
        return new CmsDbContext(options.Options);
    }

    internal static void Configure(DbContextOptionsBuilder options, IDataProvider provider, SchemaModel schema, Assembly migrationsAssembly, PluginModels? plugins = null)
    {
        provider.ConfigureContentMigrations(options, migrationsAssembly);
        options.UseCmsModel(schema, provider.Conventions, provider.JsonDialect, plugins);

        // A model ahead of its migrations must reach the runner's validation, which names the fix; EF would throw first.
        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}
