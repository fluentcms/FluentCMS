namespace FluentCMS.Plugins;

/// <summary>The built host as a plugin sees it in <see cref="IPlugin.Configure"/>. Core implements it; a plugin only calls it.</summary>
public interface IPluginAppBuilder
{
    /// <summary>The name of the plugin, from its <see cref="FluentCmsPluginAttribute"/>.</summary>
    string Name { get; }

    /// <summary>The root services of the host.</summary>
    IServiceProvider Services { get; }
}
