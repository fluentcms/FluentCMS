namespace FluentCMS.Plugins;

/// <summary>
/// The manifest of a plugin, on its class: <c>[FluentCmsPlugin("media", Version = "1.0.0", SdkRange = "[1.0.0,2.0.0)")]</c>.
/// On an assembly, <c>[assembly: FluentCmsPlugin(typeof(MediaPlugin))]</c> names a plugin class for the host to discover
/// when the assembly is referenced; an assembly may name several.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class FluentCmsPluginAttribute : Attribute
{
    /// <summary>The manifest of the class it is on.</summary>
    /// <param name="name">The name of the plugin: lower case letters and digits joined by single hyphens, starting with a letter, up to 32 characters, such as <c>media</c> or <c>ai-assist</c>.</param>
    public FluentCmsPluginAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Names a plugin class of the assembly it is on, for discovery.</summary>
    /// <param name="pluginType">The plugin class, which carries its own manifest.</param>
    public FluentCmsPluginAttribute(Type pluginType)
    {
        PluginType = pluginType;
    }

    /// <summary>The name of the plugin, or null on an assembly.</summary>
    public string? Name { get; }

    /// <summary>The plugin class an assembly names, or null on a class.</summary>
    public Type? PluginType { get; }

    /// <summary>
    /// The version of the plugin, a semantic version such as <c>1.2.0</c>. Left out, it is the informational version of the
    /// plugin's assembly.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The versions of <c>FluentCMS.Sdk</c> the plugin runs on, in NuGet interval notation, such as <c>[1.0.0,2.0.0)</c>.
    /// Required. A pre-release of the SDK counts as the release it leads to, except against a bound that is a pre-release itself.
    /// </summary>
    public string? SdkRange { get; set; }

    /// <summary>The names of the plugins this one depends on, which run before it.</summary>
    public string[] DependsOn { get; set; } = [];
}
