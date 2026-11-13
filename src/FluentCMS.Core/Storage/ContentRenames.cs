using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FluentCMS.Core.Storage;

// A column of a content table that a renamed field had, and the column it has now.
internal sealed record ColumnRename(string Table, string From, string To);

// A path under which cms_reference keeps the references of a content type's field, and the path it has now.
internal sealed record PathRename(string Type, string From, string To);

// What the fields renamed with [FormerNames] between two schemas move in storage: the column of each field and of each
// field of a single component it is, in the shared table or the locale table, and the path that cms_reference keeps its
// references under. A field of a block or of a component kept in JSON has its values under its name inside the JSON of
// another field, which nothing renames.
internal sealed class ContentRenames
{
    private static readonly string SourceTypeColumn = StorageNames.Column(nameof(Reference.SourceType));
    private static readonly string FieldColumn = StorageNames.Column(nameof(Reference.Field));

    private ContentRenames(IReadOnlyList<ColumnRename> columns, IReadOnlyList<PathRename> paths, IReadOnlyList<SchemaChange> inJson)
    {
        Columns = columns;
        Paths = paths;
        InJson = inJson;
    }

    public static ContentRenames None { get; } = new([], [], []);

    public IReadOnlyList<ColumnRename> Columns { get; }

    public IReadOnlyList<PathRename> Paths { get; }

    // The renames whose values are kept in JSON under the former name.
    public IReadOnlyList<SchemaChange> InJson { get; }

    // The renames among the changes from an earlier schema to this one, as SchemaSnapshotDiff gives them.
    public static ContentRenames Of(SchemaModel schema, IEnumerable<SchemaChange> changes)
    {
        List<SchemaChange> renames = [.. changes.Where(change => change.Kind == SchemaChangeKind.FieldRenamed)];
        if (renames.Count == 0)
        {
            return None;
        }

        var formers = renames.ToDictionary(change => (change.Family, change.Type, change.Field!), change => change.FormerField!);
        List<ColumnRename> columns = [];
        List<PathRename> paths = [];
        foreach (var type in schema.ContentTypes)
        {
            foreach (var leaf in FieldStorage.Of(schema, type))
            {
                var path = leaf.Location[(type.Name.Length + 1)..];
                var (formerPath, formerColumn) = FormerOf(schema, type, path, formers);
                if (formerPath == path)
                {
                    continue;
                }

                if (leaf.Kind != FieldStorageKind.Reference)
                {
                    columns.Add(new ColumnRename(leaf.Localized ? StorageNames.LocaleTable(type) : StorageNames.Table(type), formerColumn, leaf.Column));
                }

                if (HoldsReferences(leaf))
                {
                    paths.Add(new PathRename(type.Name, formerPath, path));
                }
            }
        }

        var componentsInJson = ComponentsInJson(schema);
        return new ContentRenames(
            columns,
            paths,
            [.. renames.Where(change => change.Family == SchemaSnapshotDiff.BlockFamily || (change.Family == SchemaSnapshotDiff.ComponentFamily && componentsInJson.Contains(change.Type)))]);
    }

    // A field whose references cms_reference keeps under its path: a relation or media field, and one whose JSON may hold
    // references, as EntryStore collects them.
    public static bool HoldsReferences(StoredField leaf) =>
        leaf.Kind == FieldStorageKind.Reference
        || (leaf.Kind == FieldStorageKind.Json && leaf.Field.FieldType is FieldTypeNames.RichText or FieldTypeNames.Blocks or FieldTypeNames.Component);

    // Moves the references of a type kept under one path to another. The SQL takes the store types from the model it is
    // generated with, which maps cms_reference: a migration's target model, or the content model in schema apply.
    public static UpdateDataOperation MoveReferences(string type, string from, string to) => new()
    {
        Table = StorageNames.Reference,
        KeyColumns = [SourceTypeColumn, FieldColumn],
        KeyValues = new object?[,] { { type, from } },
        Columns = [FieldColumn],
        Values = new object?[,] { { to } },
    };

    // The path and the column of a leaf under the names its field and the components above it had.
    private static (string Path, string Column) FormerOf(SchemaModel schema, SchemaType type, string path, Dictionary<(string Family, string Type, string Field), string> formers)
    {
        var segments = path.Split('.');
        var (family, owner) = (SchemaSnapshotDiff.ContentFamily, type);
        for (var i = 0; i < segments.Length; i++)
        {
            var name = segments[i];
            segments[i] = formers.GetValueOrDefault((family, owner.Name, name), name);
            if (i < segments.Length - 1)
            {
                (family, owner) = (SchemaSnapshotDiff.ComponentFamily, schema.FindComponent((string)owner.FindField(name)!.Settings[FieldSettings.Component])!);
            }
        }

        return (string.Join('.', segments), string.Join('_', segments.Select(StorageNames.Column)));
    }

    // A component is kept in JSON when a field of it is a list, or belongs to a block or to a component kept in JSON.
    private static HashSet<string> ComponentsInJson(SchemaModel schema)
    {
        var inJson = new HashSet<string>(StringComparer.Ordinal);
        bool added;
        do
        {
            IEnumerable<(SchemaType Owner, bool Json)> owners =
            [
                .. schema.ContentTypes.Select(type => (type, false)),
                .. schema.Components.Select(component => (component, inJson.Contains(component.Name))),
                .. schema.Blocks.Select(block => (block, true)),
            ];

            added = false;
            foreach (var (owner, json) in owners)
            {
                foreach (var field in owner.Fields.Where(field => field.FieldType == FieldTypeNames.Component))
                {
                    if ((json || schema.FieldTypes.Find(field.FieldType)!.StorageKind(field) != FieldStorageKind.Column)
                        && inJson.Add((string)field.Settings[FieldSettings.Component]))
                    {
                        added = true;
                    }
                }
            }
        }
        while (added);

        return inJson;
    }
}
