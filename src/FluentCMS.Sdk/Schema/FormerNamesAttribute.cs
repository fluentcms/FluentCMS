namespace FluentCMS.Schema;

/// <summary>Records the earlier names of a property, so that a rename is not taken for a drop and an add.</summary>
/// <param name="names">The earlier property names.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FormerNamesAttribute(params string[] names) : Attribute
{
    /// <summary>The earlier names.</summary>
    public IReadOnlyList<string> Names { get; } = names;
}
