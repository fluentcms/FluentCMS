namespace FluentCMS.Plugins;

/// <summary>
/// A plugin: code that adds to the host at compile time. The class needs <see cref="FluentCmsPluginAttribute"/> with its
/// name and a public parameterless constructor. It is added with <c>cms.AddPlugin&lt;T&gt;()</c>, or named by
/// <c>[assembly: FluentCmsPlugin(typeof(T))]</c> in a referenced assembly. The host calls both methods once, in plugin
/// order: the plugins it depends on first, then the order the plugins were added in, explicit ones before discovered ones.
/// </summary>
public interface IPlugin
{
    /// <summary>Adds the plugin's services. Runs inside <c>AddFluentCms</c>, before the services of core are added.</summary>
    /// <param name="plugin">What the plugin adds to.</param>
    void ConfigureServices(IPluginServiceBuilder plugin);

    /// <summary>Sets up what the plugin needs once the host is built. Runs inside <c>MapFluentCms</c>.</summary>
    /// <param name="app">The built host.</param>
    void Configure(IPluginAppBuilder app);
}
