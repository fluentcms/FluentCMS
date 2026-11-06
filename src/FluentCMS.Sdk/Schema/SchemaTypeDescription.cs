using FluentCMS.Publishing;

namespace FluentCMS.Schema;

/// <summary>A <see cref="SchemaType"/> as plain data.</summary>
public sealed record SchemaTypeDescription
{
    /// <summary>The name of the type, in lower kebab-case.</summary>
    public required string Name { get; init; }

    /// <summary>The kind of the type.</summary>
    public required SchemaTypeKind Kind { get; init; }

    /// <summary>Whether entries have one version per locale. Only collections and singletons can be localized.</summary>
    public bool Localized { get; init; }

    /// <summary>Whether entries belong to one site or are shared by all sites; <see langword="null"/> for components and blocks.</summary>
    public ContentScope? Scope { get; init; }

    /// <summary>What publishing does about a reference to an entry that is not published; <see langword="null"/> for components and blocks.</summary>
    public UnpublishedReferencePolicy? UnpublishedReferences { get; init; }

    /// <summary>How many versions are kept per entry and locale; <see langword="null"/> for components and blocks.</summary>
    public int? MaxVersions { get; init; }

    /// <summary>How many days an entry stays in the trash; <see langword="null"/> for components and blocks.</summary>
    public int? TrashRetentionDays { get; init; }

    /// <summary>The fields, in the order they were declared.</summary>
    public required IReadOnlyList<SchemaFieldDescription> Fields { get; init; }

    /// <summary>The reverse sides of the relations that point to this type, ordered by name.</summary>
    public IReadOnlyList<ReverseRelationDescription> ReverseRelations { get; init; } = [];
}
