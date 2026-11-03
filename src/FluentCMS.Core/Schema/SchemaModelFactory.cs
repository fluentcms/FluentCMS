using System.Collections.ObjectModel;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal static class SchemaModelFactory
{
    // Expects drafts that passed validation: unique names, relation targets that exist.
    public static SchemaModel Create(IReadOnlyList<TypeDraft> drafts, IFieldTypeRegistry fieldTypes)
    {
        var reverseRelations = drafts
            .Where(type => type.IsContentType)
            .SelectMany(type => type.Fields
                .Where(field => field.Relation is { Reverse: not null })
                .Select(field => (Target: field.Relation!.Target, Relation: new ReverseRelation { Name = field.Relation.Reverse!, SourceType = type.Name, SourceField = field.Name })))
            .ToLookup(entry => entry.Target, entry => entry.Relation);

        return new SchemaModel(drafts.Select(draft => new SchemaType
        {
            Name = draft.Name,
            Kind = draft.Kind,
            Localized = draft.Localized,
            Scope = draft.Scope,
            UnpublishedReferences = draft.UnpublishedReferences,
            MaxVersions = draft.MaxVersions,
            TrashRetentionDays = draft.TrashRetentionDays,
            ClrType = draft.ClrType,
            Fields = draft.Fields.Select(Create).ToList().AsReadOnly(),
            ReverseRelations = (draft.IsContentType ? reverseRelations[draft.Name] : []).OrderBy(relation => relation.Name, StringComparer.Ordinal).ToList().AsReadOnly(),
        }), fieldTypes);
    }

    public static SchemaField Create(FieldDraft draft) => new()
    {
        Name = draft.Name,
        FieldType = draft.FieldType,
        Multiple = draft.Multiple,
        Required = draft.Required,
        Unique = draft.Unique,
        Indexed = draft.Indexed,
        Searchable = draft.Searchable,
        ReadOnly = draft.ReadOnly,
        Hidden = draft.Hidden,
        Localizable = draft.Localizable,
        FormerNames = Array.AsReadOnly(draft.FormerNames),
        Display = draft.Display,
        DefaultValue = draft.DefaultValue,
        MinLength = draft.MinLength,
        MaxLength = draft.MaxLength,
        Minimum = draft.Minimum,
        Maximum = draft.Maximum,
        Pattern = draft.Pattern,
        VisibleWhen = draft.VisibleWhen,
        RequiredWhen = draft.RequiredWhen,
        Relation = draft.Relation,
        Settings = new ReadOnlyDictionary<string, object>(new SortedDictionary<string, object>(draft.Settings, StringComparer.Ordinal)),
    };
}
