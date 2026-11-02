namespace FluentCMS.Schema;

/// <summary>Marks a <c>BlockList</c> property as a blocks field and names the block classes it accepts.</summary>
/// <param name="blockTypes">The classes marked with <see cref="BlockAttribute"/> that the list may hold.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BlocksAttribute(params Type[] blockTypes) : FieldAttribute("blocks")
{
    /// <summary>The block classes the list may hold.</summary>
    public IReadOnlyList<Type> BlockTypes { get; } = blockTypes;
}
