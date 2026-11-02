using System.Globalization;
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
    string? DefaultValue = null);

// Where a field lives comes from IFieldType.StorageKind of the registry the schema was built with. This
// class adds what the registry does not say: the CLR type and length defaults of the core types. A plugin
// type that stores a column gets a nullable string, one that stores JSON a nullable JSON string.
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

        var fieldType = schema.FieldTypes.Find(field.FieldType)
            ?? throw new InvalidOperationException($"Field {location} has type '{field.FieldType}', which is not registered.");
        var kind = fieldType.StorageKind(field);

        if (field.FieldType == FieldTypeNames.Component && kind == FieldStorageKind.Column)
        {
            var component = schema.FindComponent((string)field.Settings[FieldSettings.Component])!;
            return component.Fields.SelectMany(inner => Flatten(schema, inner, location, column + "_", flattened: true, localized));
        }

        return [Map(field, kind, location, column, flattened, localized)];
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

        StoredField Text(bool required = false, int? maxLength = null) =>
            new(field, location, column, kind, typeof(string), localized, required && !flattened, field.MaxLength ?? maxLength);

        StoredField Value(Type clrType) =>
            new(field, location, column, kind, clrType, localized);

        return field.FieldType switch
        {
            FieldTypeNames.Text or FieldTypeNames.LongText => Text(field.Required),
            FieldTypeNames.Slug => Text(required: true, SlugLength),
            FieldTypeNames.Email => Text(maxLength: EmailLength),
            FieldTypeNames.Url => Text(maxLength: UrlLength),
            FieldTypeNames.Select => Text(maxLength: SelectColumnLength(field)),
            FieldTypeNames.Integer => Value(typeof(long?)),
            FieldTypeNames.Decimal => Value(typeof(decimal?)) with { Precision = precision, Scale = scale },
            FieldTypeNames.Boolean => Value(typeof(bool?)),
            FieldTypeNames.Date => Value(typeof(DateOnly?)),
            FieldTypeNames.DateTime => Value(typeof(DateTimeOffset?)),
            _ => Text(),
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
