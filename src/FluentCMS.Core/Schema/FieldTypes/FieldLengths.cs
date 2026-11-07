using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// The longest value a field takes: its own maximum, or the default of its type (spec 4.3). The column is sized with it and
// validation refuses a longer value, so no provider sees one. text, longText and the types of plugins have no default.
internal static class FieldLengths
{
    public const int Slug = 200;
    public const int Url = 2048;
    public const int Select = 64;

    // The longest address SMTP carries (RFC 5321), so a longer one is not an address at all.
    public const int Email = 254;

    public static int? Max(SchemaField field) => field.MaxLength ?? Default(field);

    // A multiple field counts items and is stored as JSON, so it has no default.
    private static int? Default(SchemaField field) => field.Multiple ? null : field.FieldType switch
    {
        FieldTypeNames.Slug => Slug,
        FieldTypeNames.Url => Url,
        FieldTypeNames.Email => Email,
        FieldTypeNames.Select => SettingValues.List(field, FieldSettings.Options).Select(option => option.Length).Append(Select).Max(),
        _ => null,
    };
}
