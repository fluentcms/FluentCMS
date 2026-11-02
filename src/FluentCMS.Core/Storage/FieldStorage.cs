using System.Globalization;
using FluentCMS.Schema;

namespace FluentCMS.Core.Storage;

internal enum StorageKind
{
    Column,
    Json,
    Reference,
}

// One leaf of a content type after single components are flattened. A Reference has no column, so no ClrType.
internal sealed record StoredField(
    SchemaField Field,
    string Location,
    string Column,
    StorageKind Kind,
    Type? ClrType,
    bool Localized,
    bool Required = false,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    string? DefaultValue = null);

// The one place that knows how a field type is stored. The field type registry (#2684) replaces the
// switch on names with IFieldType.StorageKind(field).
internal static class FieldStorage
{
    // Appendix A sizes these columns although the sample fields declare no length.
    private const int SlugLength = 200;
    private const int UrlLength = 2048;
    private const int SelectLength = 64;
    private const int EmailLength = 254;

    private const string EmptyList = "[]";

    public static IEnumerable<StoredField> Of(SchemaModel schema, SchemaType type) =>
        type.Fields.SelectMany(field => Flatten(schema, field, type.Name, "", flattened: false, field.Localizable));

    private static IEnumerable<StoredField> Flatten(SchemaModel schema, SchemaField field, string owner, string prefix, bool flattened, bool localized)
    {
        var location = $"{owner}.{field.Name}";
        var column = prefix + StorageNames.Column(field.Name);

        if (field.FieldType == "component" && !field.Multiple)
        {
            var component = schema.FindComponent((string)field.Settings[FieldSettings.Component])!;
            return component.Fields.SelectMany(inner => Flatten(schema, inner, location, column + "_", flattened: true, localized));
        }

        return [Map(field, location, column, flattened, localized)];
    }

    // Nothing is NOT NULL unless Appendix A shows it, because a draft may be incomplete and a missing
    // number or date has no honest placeholder. Text can hold '', a slug is derived, and rich text has an
    // empty document. A flattened column stays nullable because the whole component may be absent.
    private static StoredField Map(SchemaField field, string location, string column, bool flattened, bool localized)
    {
        var (precision, scale) = DecimalPrecision(field);

        StoredField Text(bool required = false, int? maxLength = null) =>
            new(field, location, column, StorageKind.Column, typeof(string), localized, required && !flattened, field.MaxLength ?? maxLength);

        StoredField Value(Type clrType) =>
            new(field, location, column, StorageKind.Column, clrType, localized);

        StoredField Json(bool required = false, string? defaultValue = null) =>
            new(field, location, column, StorageKind.Json, typeof(string), localized, required, DefaultValue: defaultValue);

        var isReference = field.FieldType is "relation" or "media";
        if (field.Multiple && !isReference)
        {
            return Json(required: true, EmptyList);
        }

        return field.FieldType switch
        {
            "relation" or "media" => new(field, location, column, StorageKind.Reference, null, localized),
            "text" or "longText" => Text(field.Required),
            "slug" => Text(required: true, SlugLength),
            "email" => Text(maxLength: EmailLength),
            "url" => Text(maxLength: UrlLength),
            "select" => Text(maxLength: SelectColumnLength(field)),
            "integer" => Value(typeof(long?)),
            "decimal" => Value(typeof(decimal?)) with { Precision = precision, Scale = scale },
            "boolean" => Value(typeof(bool?)),
            "date" => Value(typeof(DateOnly?)),
            "dateTime" => Value(typeof(DateTimeOffset?)),
            "richText" => Json(required: !flattened),
            "json" => Json(),
            "blocks" => Json(required: true, EmptyList),
            _ => throw new InvalidOperationException($"Field {location} has type '{field.FieldType}', which has no storage mapping."),
        };
    }

    private static int SelectColumnLength(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Options, out var options) && options is IEnumerable<string> list
            ? list.Select(option => option.Length).Append(SelectLength).Max()
            : SelectLength;

    // A scale counts only together with a precision; without one the provider decides both.
    private static (int? Precision, int? Scale) DecimalPrecision(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Precision, out var precision)
            ? (Convert.ToInt32(precision, CultureInfo.InvariantCulture),
                field.Settings.TryGetValue(FieldSettings.Scale, out var scale) ? Convert.ToInt32(scale, CultureInfo.InvariantCulture) : 0)
            : (null, null);
}
