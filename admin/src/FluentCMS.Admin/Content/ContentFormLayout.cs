using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Content;

internal sealed record FormGroup(string? Name, IReadOnlyList<SchemaFieldDescription> Fields);

// A tab of the form; the one without a name holds the fields that name no tab and is shown above the tabs.
internal sealed record FormTab(string? Name, IReadOnlyList<FormGroup> Groups)
{
    public IEnumerable<SchemaFieldDescription> Fields => Groups.SelectMany(group => group.Fields);
}

// The layout of a generated form from the fields' display (spec 4.5): hidden fields are left out, fields without a group
// form the main group, which comes first, and tabs and groups follow the order of their first field.
internal static class ContentFormLayout
{
    public const int Columns = 12;

    public static IReadOnlyList<FormTab> Build(SchemaTypeDescription type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var visible = type.Fields.Where(field => !field.Hidden).ToList();
        return
        [
            .. visible.GroupBy(field => Named(field.Display?.Tab))
                .OrderBy(tab => tab.Key is null ? 0 : 1)
                .Select(tab => new FormTab(tab.Key, Groups(tab))),
        ];
    }

    // The fields in the order the form shows them, which decides the first field with an error.
    public static IEnumerable<SchemaFieldDescription> InOrder(IReadOnlyList<FormTab> tabs) => tabs.SelectMany(tab => tab.Fields);

    // A width of 1 to 12 columns spans that many; 0, or one out of range, leaves the field the whole row.
    public static int Span(SchemaFieldDescription field) =>
        field.Display?.Width is >= 1 and <= Columns and var width ? width : Columns;

    // A new entry starts from each field's defaultValue. A read-only field is the server's to set, so a create leaves it out.
    public static JsonObject Defaults(SchemaTypeDescription type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var fields = new JsonObject();
        foreach (var field in type.Fields.Where(field => !field.Hidden && !field.ReadOnly && field.DefaultValue is not null))
        {
            fields[field.Name] = field.DefaultValue!.DeepClone();
        }

        return fields;
    }

    // The top-level field an error's path is in: sections[0].text is in sections and seo.metaTitle in seo. An empty path is
    // about the entry as a whole.
    public static string? FieldOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var end = path.IndexOfAny(['.', '[']);
        var name = end < 0 ? path : path[..end];
        return name.Length == 0 ? null : name;
    }

    private static string? Named(string? name) => string.IsNullOrWhiteSpace(name) ? null : name;

    private static List<FormGroup> Groups(IEnumerable<SchemaFieldDescription> fields) =>
    [
        .. fields.GroupBy(field => Named(field.Display?.Group))
            .OrderBy(group => group.Key is null ? 0 : 1)
            .Select(group => new FormGroup(group.Key, [.. group])),
    ];
}
