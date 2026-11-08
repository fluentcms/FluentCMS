using System.Reflection;
using FluentCMS.Core.Storage;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Plugins;

// For `dotnet ef migrations add` on a plugin's stream: the context a host gives the plugin, built in the design-time factory
// of a project that references the plugin and a data provider. See docs/plugin-migrations.md.
public static class PluginDbContextDesignTime
{
    public static PluginDbContext<TPlugin> Create<TPlugin>(IDataProvider provider, params IPluginModelContributor[] contributors)
        where TPlugin : class, IPlugin
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new PluginDbContext<TPlugin>(Options<TPlugin>(provider, contributors));
    }

    // The context of the provider whose set the factory's arguments name, which `dotnet ef` passes after `--`, so that
    // `-- PostgreSql` scaffolds into the PostgreSql set.
    public static PluginDbContext<TPlugin> Create<TPlugin>(string[] args, IEnumerable<IDataProvider> providers, params IPluginModelContributor[] contributors)
        where TPlugin : class, IPlugin
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(providers);

        var sets = providers.Select(provider => Options<TPlugin>(provider, contributors))
            .Select(options => (Set: options.FindExtension<PluginMigrationSetExtension>()?.Set, Options: options))
            .ToList();

        return args is [var name] && sets.FirstOrDefault(set => string.Equals(set.Set, name, StringComparison.OrdinalIgnoreCase)).Options is { } chosen
            ? new PluginDbContext<TPlugin>(chosen)
            : throw new ArgumentException(
                $"Name the migration set after --, one of {string.Join(", ", sets.Select(set => set.Set))}: dotnet ef migrations add <name> ... --output-dir Migrations/<set> -- <set>.",
                nameof(args));
    }

    private static DbContextOptions<PluginDbContext<TPlugin>> Options<TPlugin>(IDataProvider provider, IPluginModelContributor[] contributors)
        where TPlugin : class, IPlugin
    {
        ArgumentNullException.ThrowIfNull(contributors);

        var name = typeof(TPlugin).GetCustomAttribute<FluentCmsPluginAttribute>(inherit: false)?.Name
            ?? throw new InvalidOperationException($"{typeof(TPlugin).FullName} has no [FluentCmsPlugin] attribute.");
        var options = new DbContextOptionsBuilder<PluginDbContext<TPlugin>>();
        PluginStorage.Configure(options, provider, PluginModels.Of(contributors), typeof(TPlugin).Assembly, PluginStorage.HistoryTableOf(name));
        return options.Options;
    }
}
