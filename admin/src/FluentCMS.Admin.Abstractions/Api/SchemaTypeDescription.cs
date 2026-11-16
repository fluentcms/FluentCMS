namespace FluentCMS.Admin.Api;

/// <summary>A content type, component or block of a <see cref="SchemaDescription"/>.</summary>
public sealed record SchemaTypeDescription
{
    /// <summary>The name, in lower kebab-case.</summary>
    public required string Name { get; init; }

    /// <summary>The kind: <c>collection</c>, <c>singleton</c>, <c>component</c> or <c>block</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Whether entries have one version per locale.</summary>
    public bool Localized { get; init; }

    /// <summary>Whether entries belong to one site (<c>site</c>) or to all (<c>global</c>); <see langword="null"/> for components and blocks.</summary>
    public string? Scope { get; init; }

    /// <summary>What publishing does about a reference to an entry that is not published; <see langword="null"/> for components and blocks.</summary>
    public string? UnpublishedReferences { get; init; }

    /// <summary>How many versions are kept per entry and locale; <see langword="null"/> for components and blocks.</summary>
    public int? MaxVersions { get; init; }

    /// <summary>How many days an entry stays in the trash; <see langword="null"/> for components and blocks.</summary>
    public int? TrashRetentionDays { get; init; }

    /// <summary>The fields, in the order they were declared.</summary>
    public required IReadOnlyList<SchemaFieldDescription> Fields { get; init; }

    /// <summary>The reverse sides of the relations that point to this type.</summary>
    public IReadOnlyList<ReverseRelationDescription> ReverseRelations { get; init; } = [];
}
