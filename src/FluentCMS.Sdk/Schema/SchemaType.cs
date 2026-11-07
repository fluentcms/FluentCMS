using System.Collections.Frozen;
using FluentCMS.Publishing;

namespace FluentCMS.Schema;

/// <summary>A collection, singleton, component or block of a <see cref="SchemaModel"/>.</summary>
public sealed class SchemaType
{
    private readonly FrozenDictionary<string, SchemaField> _fieldsByName = FrozenDictionary<string, SchemaField>.Empty;

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

    /// <summary>How many days an entry stays in the trash before the purge job deletes it for good.</summary>
    public int TrashRetentionDays { get; internal init; } = PublishPolicyAttribute.DefaultTrashRetentionDays;

    /// <summary>The class the type was read from, or <see langword="null"/> if it was defined without one. It does not take part in comparing models.</summary>
    public Type? ClrType { get; internal init; }

    /// <summary>The fields, in the order they were declared.</summary>
    public IReadOnlyList<SchemaField> Fields
    {
        get;
        internal init
        {
            field = value;

            // The first of a repeated name wins, as a scan of the list finds it. Validation refuses a repeat before a model is built.
            Dictionary<string, SchemaField> byName = new(StringComparer.Ordinal);
            foreach (var item in value)
            {
                byName.TryAdd(item.Name, item);
            }

            _fieldsByName = byName.ToFrozenDictionary(StringComparer.Ordinal);
        }
    } = [];

    /// <summary>The reverse sides of the relations that point to this type, ordered by name.</summary>
    public IReadOnlyList<ReverseRelation> ReverseRelations { get; internal init; } = [];

    /// <summary>Finds a field by name.</summary>
    /// <param name="name">The field name, in camelCase.</param>
    /// <returns>The field, or <see langword="null"/> if there is none with that name.</returns>
    public SchemaField? FindField(string name) => name is not null && _fieldsByName.TryGetValue(name, out var field) ? field : null;
}
