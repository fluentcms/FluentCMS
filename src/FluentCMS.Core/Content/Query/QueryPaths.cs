using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

internal abstract record ResolvedPath;

internal sealed record SystemPath(SystemField Field) : ResolvedPath;

// A single component, whose fields live in columns of their own.
internal sealed record GroupPath(SchemaType Component) : ResolvedPath;

// A stored field and the segments below it. ApiPath is the field's path within its type, such as seo.metaTitle, and
// is also its name in cms_reference.
internal sealed record FieldPath(StoredField Stored, string ApiPath, string[] Rest) : ResolvedPath;

// Reads a dotted path of a type. A path that does not exist and one the principal may not read look the same, so
// that a client cannot probe for a field. Trusted paths, which come from a row rule, skip the access checks.
internal sealed class QueryPaths(SchemaModel schema, IContentQueryAccess access)
{
    public ResolvedPath? Resolve(SchemaType type, string path, bool trusted = false)
    {
        var segments = string.IsNullOrEmpty(path) ? [""] : path.Split('.');
        if (!segments.All(JsonScalar.IsField))
        {
            return null;
        }

        if (segments.Length == 1 && SystemField.Find(path) is { } system)
        {
            return trusted || access.CanReadField(type, path) ? new SystemPath(system) : null;
        }

        if (!trusted && !access.CanReadField(type, segments[0]))
        {
            return null;
        }

        var owner = type;
        for (var index = 0; index < segments.Length; index++)
        {
            var field = owner.FindField(segments[index]);
            if (field is null || (field.Hidden && !trusted))
            {
                return null;
            }

            if (field.FieldType != FieldTypeNames.Component || field.Multiple)
            {
                var apiPath = string.Join('.', segments[..(index + 1)]);
                return new FieldPath(FieldStorage.ByPath(schema, type)[apiPath], apiPath, segments[(index + 1)..]);
            }

            owner = schema.FindComponent((string)field.Settings[FieldSettings.Component])!;
        }

        return new GroupPath(owner);
    }

    public StoredField Field(SchemaType type, string apiPath) => FieldStorage.ByPath(schema, type)[apiPath];

    public static string ApiPath(SchemaType type, StoredField stored) => stored.Location[(type.Name.Length + 1)..];
}
