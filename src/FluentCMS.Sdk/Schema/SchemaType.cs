using FluentCMS.Publishing;

namespace FluentCMS.Schema;

/// <summary>A collection, singleton, component or block of a <see cref="SchemaModel"/>.</summary>
public sealed class SchemaType
{
    internal SchemaType()
    {
    }

    /// <summary>The name of the type, in lower kebab-case.</summary>
    public string Name { get; internal init; } = "";

    /// <summary>The kind of the type.</summary>
    public SchemaTypeKind Kind { get; internal init; }

    /// <summary>Whether entries have one version per locale. Only collections and singletons can be localized.</summary>
    public bool Localized { get; internal init; }

    /// <summary>Whether entries belong to one site or are shared by all sites. Always <see cref="ContentScope.Site"/> for components and blocks.</summary>
    public ContentScope Scope { get; internal init; }

    /// <summary>What publishing an entry does about a reference to an entry that is not published or is in the trash.</summary>
    public UnpublishedReferencePolicy UnpublishedReferences { get; internal init; }

    /// <summary>How many versions to keep per entry and locale. A version that was published is never pruned.</summary>
    public int MaxVersions { get; internal init; } = PublishPolicyAttribute.DefaultMaxVersions;

    /// <summary>The class the type was read from, or <see langword="null"/> if it was defined without one. It does not take part in comparing models.</summary>
    public Type? ClrType { get; internal init; }

    /// <summary>The fields, in the order they were declared.</summary>
    public IReadOnlyList<SchemaField> Fields { get; internal init; } = [];

    /// <summary>The reverse sides of the relations that point to this type, ordered by name.</summary>
    public IReadOnlyList<ReverseRelation> ReverseRelations { get; internal init; } = [];

    /// <summary>Finds a field by name.</summary>
    /// <param name="name">The field name, in camelCase.</param>
    /// <returns>The field, or <see langword="null"/> if there is none with that name.</returns>
    public SchemaField? FindField(string name)
    {
        foreach (var field in Fields)
        {
            if (field.Name == name)
            {
                return field;
            }
        }

        return null;
    }
}
