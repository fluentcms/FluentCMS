namespace FluentCMS.Core.Schema.FieldTypes;

internal static class FieldTypeNames
{
    public const string Text = "text";
    public const string LongText = "longText";
    public const string RichText = "richText";
    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string DateTime = "dateTime";
    public const string Email = "email";
    public const string Url = "url";
    public const string Slug = "slug";
    public const string Select = "select";
    public const string Json = "json";
    public const string Relation = "relation";
    public const string Component = "component";
    public const string Blocks = "blocks";

    // The rule of IFieldType.Name, which generate holds a schema snapshot to before it writes a field type into code.
    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) && char.IsAsciiLetter(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
