using System.Reflection;

namespace FluentCMS.Plugins;

/// <summary>The tables a plugin adds in <see cref="IPlugin.ConfigureServices"/>.</summary>
public static class PluginServiceBuilderExtensions
{
    /// <summary>
    /// Adds a model contributor. Its tables join the runtime model of core and the plugin's
    /// <see cref="PluginDbContext{TPlugin}"/>. A table it maps outside <c>cms_{plugin}_</c> stops the host.
    /// </summary>
    /// <typeparam name="TContributor">The contributor, created once with its parameterless constructor.</typeparam>
    /// <param name="plugin">The builder of the plugin.</param>
    /// <returns>The builder.</returns>
    public static IPluginServiceBuilder AddModel<TContributor>(this IPluginServiceBuilder plugin)
        where TContributor : class, IPluginModelContributor, new()
    {
        Storage(plugin).AddModel(new TContributor());
        return plugin;
    }

    /// <summary>
    /// Adds the plugin's migration stream: the migrations of the assembly that name <see cref="PluginDbContext{TPlugin}"/>,
    /// with the history table <c>cms_{plugin}__migrations</c>. The plugins' streams run after the system stream and before
    /// the content stream, in plugin order.
    /// </summary>
    /// <param name="plugin">The builder of the plugin.</param>
    /// <param name="assembly">The assembly that holds the migrations, usually the plugin's own.</param>
    /// <returns>The builder.</returns>
    public static IPluginServiceBuilder AddMigrations(this IPluginServiceBuilder plugin, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Storage(plugin).AddMigrations(assembly);
        return plugin;
    }

    private static IPluginStorageBuilder Storage(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        return plugin as IPluginStorageBuilder
            ?? throw new InvalidOperationException($"{plugin.GetType().FullName} is not the plugin builder of a FluentCMS host, which is the only one that adds tables.");
    }
}
