namespace FluentCMS.Infrastructure.Plugins.Abstractions;

/// <summary>
/// Attribute that marks a class as a plugin startup class.
/// Classes marked with this attribute will be automatically discovered and loaded by the plugin system.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PluginAttribute : Attribute
{
    // This attribute currently has no properties, but can be extended in the future
    // for metadata like plugin categories, dependencies, or loading priorities
}
