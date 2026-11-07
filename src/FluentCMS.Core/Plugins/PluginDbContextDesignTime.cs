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
        ArgumentNullException.ThrowIfNull(contributors);

        var name = typeof(TPlugin).GetCustomAttribute<FluentCmsPluginAttribute>(inherit: false)?.Name
            ?? throw new InvalidOperationException($"{typeof(TPlugin).FullName} has no [FluentCmsPlugin] attribute.");
        var options = new DbContextOptionsBuilder<PluginDbContext<TPlugin>>();
        PluginStorage.Configure(options, provider, PluginModels.Of(contributors), typeof(TPlugin).Assembly, PluginStorage.HistoryTableOf(name));
        return new PluginDbContext<TPlugin>(options.Options);
    }
}
