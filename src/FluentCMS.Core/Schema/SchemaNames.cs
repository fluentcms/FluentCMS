using System.Text.RegularExpressions;

namespace FluentCMS.Core.Schema;

// Names that end up in URLs, entry documents and definitions; field types use the same rules, so a plugin checks them like a built-in.
public static partial class SchemaNames
{
    public const int MaxLength = 64;

    // Lowercase words joined by single hyphens, such as "product-page".
    public static bool IsSlug(string? value)
    {
        return value is { Length: > 0 and <= MaxLength } && SlugPattern().IsMatch(value);
    }

    // A key in every entry of the type, such as "price" or "valid_until".
    public static bool IsFieldName(string? value)
    {
        return value is { Length: > 0 and <= MaxLength } && FieldNamePattern().IsMatch(value);
    }

    // "text" for a built-in; a plugin prefixes its own, such as "acme.rating".
    public static bool IsFieldTypeName(string? value)
    {
        return value is { Length: > 0 and <= MaxLength } && FieldTypeNamePattern().IsMatch(value);
    }

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldTypeNamePattern();
}
