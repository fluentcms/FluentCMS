namespace FluentCMS.Schema;

/// <summary>Marks a class as a block: a member of the union of a blocks field. Its name is the block's <c>type</c> in the API.</summary>
/// <param name="name">The block name; by default the class name in lower kebab-case.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockAttribute(string? name = null) : Attribute
{
    /// <summary>The block name, or <see langword="null"/> to use the class name.</summary>
    public string? Name { get; } = name;
}
