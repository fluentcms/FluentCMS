namespace FluentCMS.Schema;

/// <summary>The reverse side of a relation: the entries of another type that point to an entry of this one.</summary>
public sealed class ReverseRelation
{
    internal ReverseRelation()
    {
    }

    /// <summary>The name of the reverse side on the type that is pointed to.</summary>
    public string Name { get; internal init; } = "";

    /// <summary>The name of the content type that holds the relation field.</summary>
    public string SourceType { get; internal init; } = "";

    /// <summary>The name of the relation field on <see cref="SourceType"/>.</summary>
    public string SourceField { get; internal init; } = "";
}
