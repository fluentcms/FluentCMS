namespace FluentCMS.Schema;

/// <summary>A <see cref="ReverseRelation"/> as plain data.</summary>
public sealed record ReverseRelationDescription
{
    /// <summary>The name of the reverse side on the type that is pointed to.</summary>
    public required string Name { get; init; }

    /// <summary>The name of the content type that holds the relation field.</summary>
    public required string SourceType { get; init; }

    /// <summary>The name of the relation field on <see cref="SourceType"/>.</summary>
    public required string SourceField { get; init; }
}
