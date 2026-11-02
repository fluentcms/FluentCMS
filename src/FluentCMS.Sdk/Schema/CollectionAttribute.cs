namespace FluentCMS.Schema;

/// <summary>Marks a class that derives from <see cref="ContentItem"/> as a collection: a content type with many entries.</summary>
/// <param name="name">The type name; by default the class name in lower kebab-case.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CollectionAttribute(string? name = null) : Attribute
{
    /// <summary>The type name, or <see langword="null"/> to use the class name.</summary>
    public string? Name { get; } = name;

    /// <summary>Whether entries have one version per locale.</summary>
    public bool Localized { get; set; }

    /// <summary>Whether entries belong to one site or are shared by all sites.</summary>
    public ContentScope Scope { get; set; }
}
