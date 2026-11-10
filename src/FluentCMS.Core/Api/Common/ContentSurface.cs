using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Schema;

namespace FluentCMS.Core.Api.Common;

// The SDK's seam for a surface over content that is not REST, such as the GraphQL plugin: it answers with the JSON, the
// limits and the access questions the REST APIs use, so that the two cannot drift apart.
internal sealed class ContentSurface(
    SchemaModel schema,
    ContentQueryOptions options,
    IContentQueryAccess access,
    IOperationContextAccessor operations) : IContentSurface
{
    public int DefaultPageSize => options.DefaultPageSize;

    public int MaxPageSize => options.MaxPageSize;

    public int MaxPopulateDepth => options.MaxPopulateDepth;

    public async ValueTask<IReadOnlySet<string>?> ReadableFieldsAsync(string type, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var operation = operations.Current ?? throw new InvalidOperationException("What a principal may read is asked inside an operation.");
        if (schema.FindContentType(type) is not { } found)
        {
            return null;
        }

        var query = await access.ForQueryAsync(operation.Site, locale ?? operation.Locale, cancellationToken);
        if (!query.CanReadType(found))
        {
            return null;
        }

        return SystemField.Fields.Select(field => field.Name)
            .Concat(found.Fields.Where(field => !field.Hidden).Select(field => field.Name))
            .Where(name => query.CanReadField(found, name))
            .ToHashSet(StringComparer.Ordinal);
    }

    public JsonObject ToJson(ContentDocument entry) => ContentJson.Entry(entry);
}
