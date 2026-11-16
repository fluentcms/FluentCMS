namespace FluentCMS.Admin.Api;

/// <summary>The schema as the caller may see it, from <c>GET /schema</c>, which drives the generated lists and forms.</summary>
public sealed record SchemaDescription
{
    /// <summary>The collections and singletons, ordered by name.</summary>
    public required IReadOnlyList<SchemaTypeDescription> Types { get; init; }

    /// <summary>The components, ordered by name.</summary>
    public required IReadOnlyList<SchemaTypeDescription> Components { get; init; }

    /// <summary>The blocks, ordered by name.</summary>
    public required IReadOnlyList<SchemaTypeDescription> Blocks { get; init; }
}
