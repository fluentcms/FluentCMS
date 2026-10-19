using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

public static class CheckNames
{
    // A check is named like a content type, such as "no-superlatives".
    public static bool IsName(string? value)
    {
        return SchemaNames.IsSlug(value);
    }

    // A check type is named like a field type: "terms" for a built-in, and a plugin prefixes its own, such as "acme.readability".
    public static bool IsTypeName(string? value)
    {
        return SchemaNames.IsFieldTypeName(value);
    }
}
