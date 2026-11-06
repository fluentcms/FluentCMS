using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins;

/// <summary>
/// What a plugin adds to while the host is set up, in <see cref="IPlugin.ConfigureServices"/>. Core implements it; a plugin
/// only calls it.
/// </summary>
public interface IPluginServiceBuilder
{
    /// <summary>The name of the plugin, from its <see cref="FluentCmsPluginAttribute"/>.</summary>
    string Name { get; }

    /// <summary>
    /// The services of the host. A service the plugin adds here is added before those of core, so it replaces a core service
    /// that core adds only when none is there.
    /// </summary>
    IServiceCollection Services { get; }
}
