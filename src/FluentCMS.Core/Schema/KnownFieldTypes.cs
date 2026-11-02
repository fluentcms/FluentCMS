using System.Collections.Frozen;

namespace FluentCMS.Core.Schema;

// The field types core registers. The field type registry replaces this list.
internal static class KnownFieldTypes
{
    public const string Select = "select";
    public const string Slug = "slug";
    public const string Relation = "relation";
    public const string Component = "component";
    public const string Blocks = "blocks";

    public static IReadOnlySet<string> Core { get; } = new[]
    {
        "text",
        "longText",
        "richText",
        "integer",
        "decimal",
        "boolean",
        "date",
        "dateTime",
        "email",
        "url",
        Slug,
        Select,
        "json",
        Relation,
        Component,
        Blocks,
    }.ToFrozenSet(StringComparer.Ordinal);
}
