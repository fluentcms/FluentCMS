namespace FluentCMS.Admin.Api;

/// <summary>The reverse side of a relation that points to a type.</summary>
public sealed record ReverseRelationDescription
{
    /// <summary>The name of the reverse side.</summary>
    public required string Name { get; init; }

    /// <summary>The type whose field is the relation.</summary>
    public required string SourceType { get; init; }

    /// <summary>The relation field of <see cref="SourceType"/>.</summary>
    public required string SourceField { get; init; }
}
