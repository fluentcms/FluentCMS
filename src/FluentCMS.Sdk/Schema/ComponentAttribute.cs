namespace FluentCMS.Schema;

/// <summary>
/// On a class, marks it as a component: a reusable group of fields. On a property, marks it as a component
/// field whose type is such a class, or a list of it for a repeatable component.
/// </summary>
/// <param name="name">On a class, the component name; by default the class name in lower kebab-case.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class ComponentAttribute(string? name = null) : FieldAttribute("component")
{
    /// <summary>The component name given on a class, or <see langword="null"/> to use the class name.</summary>
    public string? Name { get; } = name;
}
