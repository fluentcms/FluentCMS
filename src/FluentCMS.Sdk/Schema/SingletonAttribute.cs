namespace FluentCMS.Schema;

/// <summary>Marks a class that derives from <see cref="ContentItem"/> as a singleton: a content type with a single entry.</summary>
/// <param name="name">The type name; by default the class name in lower kebab-case.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SingletonAttribute(string? name = null) : Attribute
{
    /// <summary>The type name, or <see langword="null"/> to use the class name.</summary>
    public string? Name { get; } = name;

    /// <summary>Whether the entry has one version per locale.</summary>
    public bool Localized { get; set; }
}
