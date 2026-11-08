namespace FluentCMS.Schema;

/// <summary>
/// Marks a property of type <c>Ref&lt;T&gt;</c> or <c>RefList&lt;T&gt;</c> as a relation to a collection or singleton.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RelationAttribute() : FieldAttribute("relation")
{
    /// <summary>
    /// The name of the reverse side on the target type, for example <c>"articles"</c>. It is not a stored field but
    /// metadata that introspection shows: a filter, field selection or population that names it is refused. To read the
    /// entries that point to an entry, filter their type by this relation. Leave it unset for none.
    /// </summary>
    public string? Reverse { get; set; }

    /// <summary>Whether the order of the related entries is kept.</summary>
    public bool Ordered { get; set; }
}
