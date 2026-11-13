using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Storage;

// One leaf of a content type after single components are flattened. A Reference has no column, so no ClrType.
internal sealed record StoredField(
    SchemaField Field,
    string Location,
    string Column,
    FieldStorageKind Kind,
    Type? ClrType,
    bool Localized,
    bool Required = false,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    string? DefaultValue = null)
{
    // The columns the field had under its former names and those of the components above it, which schema apply renames.
    public IReadOnlyList<string> FormerColumns { get; init; } = [];

    // The same as paths in the type, such as meta.ogImage, which cms_reference keeps a reference under.
    public IReadOnlyList<string> FormerPaths { get; init; } = [];
}

// Where a field lives comes from IFieldType.StorageKind of the registry the schema was built with. This
// class adds what the registry does not say: the CLR type of the core types and, from FieldLengths, the length of their
// columns. A plugin type that stores a column gets a nullable string, one that stores JSON a nullable JSON string.
internal static class FieldStorage
{
    private const string EmptyList = "[]";

    // A model and its types do not change once built, so the leaves of a type are worked out once per model.
    private static readonly ConditionalWeakTable<SchemaModel, ConcurrentDictionary<SchemaType, Leaves>> Computed = new();

    public static IReadOnlyList<StoredField> Of(SchemaModel schema, SchemaType type) => LeavesOf(schema, type).All;

    // The leaves by their path in the type, such as seo.metaTitle, which is QueryPaths.ApiPath.
    public static FrozenDictionary<string, StoredField> ByPath(SchemaModel schema, SchemaType type) => LeavesOf(schema, type).ByPath;

    private static Leaves LeavesOf(SchemaModel schema, SchemaType type) =>
        Computed.GetValue(schema, static _ => new ConcurrentDictionary<SchemaType, Leaves>())
            .GetOrAdd(type, static (type, schema) =>
            {
                StoredField[] all = [.. type.Fields.SelectMany(field => Flatten(schema, field, type.Name, [("", "")], flattened: false, field.Localizable))];
                return new Leaves(all, all.ToFrozenDictionary(stored => stored.Location[(type.Name.Length + 1)..], StringComparer.Ordinal));
            }, schema);

    // The column and path prefixes of the components above the field: the current ones first, then those of their former names.
    private static IEnumerable<StoredField> Flatten(SchemaModel schema, SchemaField field, string owner, IReadOnlyList<(string Column, string Path)> prefixes, bool flattened, bool localized)
    {
        var location = $"{owner}.{field.Name}";
        (string Column, string Path)[] names = [.. prefixes.SelectMany(prefix => field.FormerNames.Prepend(field.Name).Select(name => (prefix.Column + StorageNames.Column(name), prefix.Path + name)))];
        string[] columns = [.. names.Select(name => name.Column).Distinct(StringComparer.Ordinal)];
        string[] paths = [.. names.Select(name => name.Path).Distinct(StringComparer.Ordinal)];

        var fieldType = schema.FieldTypes.Find(field.FieldType)
            ?? throw new InvalidOperationException($"Field {location} has type '{field.FieldType}', which is not registered.");
        var kind = fieldType.StorageKind(field);

        if (field.FieldType == FieldTypeNames.Component && kind == FieldStorageKind.Column)
        {
            var component = schema.FindComponent((string)field.Settings[FieldSettings.Component])!;
            return component.Fields.SelectMany(inner => Flatten(schema, inner, location, [.. names.Distinct().Select(name => (name.Column + "_", name.Path + "."))], flattened: true, localized));
        }

        return [Map(field, kind, location, columns[0], flattened, localized) with { FormerColumns = columns[1..], FormerPaths = paths[1..] }];
    }

    // Nothing is NOT NULL unless Appendix A shows it, because a draft may be incomplete and a missing
    // number or date has no honest placeholder. Text can hold '', a slug is derived, and rich text has an
    // empty document. A flattened column stays nullable because the whole component may be absent.
    private static StoredField Map(SchemaField field, FieldStorageKind kind, string location, string column, bool flattened, bool localized)
    {
        if (kind == FieldStorageKind.Reference)
        {
            return new(field, location, column, kind, null, localized);
        }

        if (kind == FieldStorageKind.Json)
        {
            // A list is never null; a rich text field has an empty document, except in a component that may be absent.
            var list = field.Multiple || field.FieldType == FieldTypeNames.Blocks;
            var required = list || (field.FieldType == FieldTypeNames.RichText && !flattened);
            return new(field, location, column, kind, typeof(string), localized, required, DefaultValue: list ? EmptyList : null);
        }

        var (precision, scale) = DecimalPrecision(field);

        StoredField Text(bool required = false) =>
            new(field, location, column, kind, typeof(string), localized, required && !flattened, FieldLengths.Max(field));

        StoredField Value(Type clrType) =>
            new(field, location, column, kind, clrType, localized);

        return field.FieldType switch
        {
            FieldTypeNames.Text or FieldTypeNames.LongText => Text(field.Required),
            FieldTypeNames.Slug => Text(required: true),
            FieldTypeNames.Integer => Value(typeof(long?)),
            FieldTypeNames.Decimal => Value(typeof(decimal?)) with { Precision = precision, Scale = scale },
            FieldTypeNames.Boolean => Value(typeof(bool?)),
            FieldTypeNames.Date => Value(typeof(DateOnly?)),
            FieldTypeNames.DateTime => Value(typeof(DateTimeOffset?)),
            _ => Text(),
        };
    }

    // A scale counts only together with a precision; without one the provider decides both.
    private static (int? Precision, int? Scale) DecimalPrecision(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Precision, out var precision)
            ? (Convert.ToInt32(precision, CultureInfo.InvariantCulture),
                field.Settings.TryGetValue(FieldSettings.Scale, out var scale) ? Convert.ToInt32(scale, CultureInfo.InvariantCulture) : 0)
            : (null, null);

    private sealed record Leaves(StoredField[] All, FrozenDictionary<string, StoredField> ByPath);
}
