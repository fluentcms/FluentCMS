namespace FluentCMS.Schema;

/// <summary>
/// Marks a property of type <c>Ref&lt;T&gt;</c> or <c>RefList&lt;T&gt;</c> as a relation to a collection or singleton.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RelationAttribute() : FieldAttribute("relation")
{
    /// <summary>
    /// The name of the reverse side on the target type, for example <c>nameof(Author.Articles)</c>. It is not a
    /// stored field; it lets clients ask for the entries that point to an entry. Leave it unset for none.
    /// </summary>
    public string? Reverse { get; set; }

    /// <summary>Whether the order of the related entries is kept.</summary>
    public bool Ordered { get; set; }
}
