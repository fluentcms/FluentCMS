using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Admin.Plugins;

/// <summary>
/// Declares the admin half of a plugin. On the plugin's class it gives the name of the server plugin, the version of the
/// half and the range of <c>FluentCMS.Admin.Abstractions</c> it was built for; on the assembly it names that class, one per
/// assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FluentCmsAdminPluginAttribute : Attribute
{
    /// <summary>Declares a class as the plugin of an admin half.</summary>
    /// <param name="name">The name of the server plugin, as <c>GET /plugins</c> lists it, such as <c>media</c>.</param>
    public FluentCmsAdminPluginAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Names the plugin of an assembly.</summary>
    /// <param name="pluginType">The class that implements <see cref="IAdminPlugin"/> and carries this attribute with its name.</param>
    public FluentCmsAdminPluginAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type pluginType)
    {
        ArgumentNullException.ThrowIfNull(pluginType);
        PluginType = pluginType;
    }

    /// <summary>Gets the name of the server plugin, on a class.</summary>
    public string? Name { get; }

    /// <summary>Gets the class of the plugin, on an assembly.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? PluginType { get; }

    /// <summary>Gets or sets the version of the admin half, such as <c>1.0.0</c>.</summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the range of <c>FluentCMS.Admin.Abstractions</c> the half was built for, in NuGet's notation, such as
    /// <c>[1.0.0,2.0.0)</c>. A half outside the running version is not loaded.
    /// </summary>
    public string? AdminRange { get; set; }
}
